using Microsoft.EntityFrameworkCore;
using PomodoroRelax4.Data;
using PomodoroRelax4.Models;

namespace PomodoroRelax4.Services;

public class StatsViewModel
{
    public int TodayPomodoros { get; set; }
    public int WeekPomodoros { get; set; }
    public int TotalPomodoros { get; set; }
    public int TotalFocusMinutes { get; set; }
    public int TasksCompleted { get; set; }
    public int TasksTotal { get; set; }
    public List<DailyStats> Last7Days { get; set; } = new();
    public List<RecentSession> RecentSessions { get; set; } = new();
}

public class DailyStats
{
    public DateOnly Date { get; set; }
    public int Pomodoros { get; set; }
    public int FocusMinutes { get; set; }
}

public class RecentSession
{
    public TimerMode SessionType { get; set; }
    public int DurationMinutes { get; set; }
    public DateTime CompletedAt { get; set; }
    public string? TaskTitle { get; set; }
}

public interface IStatsService
{
    /// <param name="utcOffset">The viewer's offset from UTC; days and weeks start at their local midnight.</param>
    Task<StatsViewModel> GetStatsAsync(string userId, TimeSpan utcOffset);
}

public class StatsService : IStatsService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public StatsService(IDbContextFactory<ApplicationDbContext> factory) => _factory = factory;

    public async Task<StatsViewModel> GetStatsAsync(string userId, TimeSpan utcOffset)
    {
        await using var db = await _factory.CreateDbContextAsync();

        // Sessions are stored in UTC; work out where the viewer's local day and week begin.
        // Kind must stay Utc for PostgreSQL's timestamptz parameters.
        var localToday = (DateTime.UtcNow + utcOffset).Date;
        var today = DateTime.SpecifyKind(localToday - utcOffset, DateTimeKind.Utc);
        var weekStart = today.AddDays(-(((int)localToday.DayOfWeek + 6) % 7)); // weeks start on Monday
        var sevenDaysAgo = today.AddDays(-6);

        // Server-side aggregates — no unbounded memory load
        var baseQuery = db.PomodoroSessions
            .Where(s => s.UserId == userId && s.SessionType == TimerMode.Pomodoro && s.CompletedAt.HasValue);

        var todayPomodoros    = await baseQuery.CountAsync(s => s.CompletedAt!.Value >= today);
        var weekPomodoros     = await baseQuery.CountAsync(s => s.CompletedAt!.Value >= weekStart);
        var totalPomodoros    = await baseQuery.CountAsync();
        var totalFocusMinutes = await baseQuery.SumAsync(s => (int?)s.DurationSeconds) / 60 ?? 0;

        // Last 7 days — load only the window needed for the bar chart
        var last7Raw = await baseQuery
            .Where(s => s.CompletedAt!.Value >= sevenDaysAgo)
            .Select(s => new { s.CompletedAt, s.DurationSeconds })
            .ToListAsync();

        var last7Days = new List<DailyStats>();
        for (int i = 6; i >= 0; i--)
        {
            var date = DateOnly.FromDateTime(localToday.AddDays(-i));
            var dayPomodoros = last7Raw.Where(s => DateOnly.FromDateTime(s.CompletedAt!.Value + utcOffset) == date).ToList();
            last7Days.Add(new DailyStats
            {
                Date = date,
                Pomodoros = dayPomodoros.Count,
                FocusMinutes = dayPomodoros.Sum(s => s.DurationSeconds) / 60
            });
        }

        // Task stats
        var tasksCompleted = await db.PomodoroTasks.CountAsync(t => t.UserId == userId && t.IsCompleted);
        var tasksTotal     = await db.PomodoroTasks.CountAsync(t => t.UserId == userId);

        // Recent sessions (all modes, last 10)
        var recentRaw = await db.PomodoroSessions
            .Where(s => s.UserId == userId && s.CompletedAt.HasValue)
            .OrderByDescending(s => s.CompletedAt)
            .Take(10)
            .ToListAsync();

        var taskIds = recentRaw
            .Where(s => s.TaskId.HasValue)
            .Select(s => s.TaskId!.Value)
            .Distinct()
            .ToList();

        var taskTitleMap = taskIds.Count > 0
            ? await db.PomodoroTasks
                .Where(t => taskIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Title)
            : new Dictionary<int, string>();

        var recentSessions = recentRaw.Select(s => new RecentSession
        {
            SessionType = s.SessionType,
            DurationMinutes = s.DurationSeconds / 60,
            CompletedAt = s.CompletedAt!.Value + utcOffset, // viewer's local time
            TaskTitle = s.TaskId.HasValue && taskTitleMap.TryGetValue(s.TaskId.Value, out var title) ? title : null
        }).ToList();

        return new StatsViewModel
        {
            TodayPomodoros    = todayPomodoros,
            WeekPomodoros     = weekPomodoros,
            TotalPomodoros    = totalPomodoros,
            TotalFocusMinutes = totalFocusMinutes,
            TasksCompleted    = tasksCompleted,
            TasksTotal        = tasksTotal,
            Last7Days         = last7Days,
            RecentSessions    = recentSessions
        };
    }
}
