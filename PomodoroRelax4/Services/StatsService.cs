using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PomodoroRelax4.Data;
using PomodoroRelax4.Models;

namespace PomodoroRelax4.Services;

// The period the activity chart, hours and top tasks cover
public enum StatsRange
{
    Week,   // the last 7 days, one bar a day
    Month,  // the last 30 days, one bar a day
    Year    // this month and the 11 before it, one bar a month
}

public class StatsViewModel
{
    public StatsRange Range { get; set; }

    public int TodayPomodoros { get; set; }
    public int TodayFocusMinutes { get; set; }
    public int TotalPomodoros { get; set; }
    public int TotalFocusMinutes { get; set; }

    // Days in a row with at least one pomodoro, ending today (or yesterday, until today has one)
    public int CurrentStreak { get; set; }
    // Longest such run within the heatmap's year
    public int BestStreak { get; set; }

    public int TasksCompleted { get; set; }
    public int TasksTotal { get; set; }
    // Pomodoros spent per pomodoro estimated, over done tasks that had any; null when there are none
    public double? EstimateRatio { get; set; }

    // The selected range and the equally long period before it
    public int RangePomodoros { get; set; }
    public int RangeFocusMinutes { get; set; }
    public int PreviousRangePomodoros { get; set; }
    public int RangeDays { get; set; }
    public DailyStats? BestDay { get; set; }
    public List<ChartBar> Bars { get; set; } = new();
    // Pomodoros by the local hour they started in, 0–23
    public int[] Hours { get; set; } = new int[24];
    public List<TaskStats> TopTasks { get; set; } = new();
    public int PomodorosWithoutTask { get; set; }

    // Pomodoros per day from HeatmapStart (a Monday) through today
    public DateOnly HeatmapStart { get; set; }
    public List<int> Heatmap { get; set; } = new();
    public int ActiveDays { get; set; }

    public List<RecentSession> RecentSessions { get; set; } = new();
}

public class DailyStats
{
    public DateOnly Date { get; set; }
    public int Pomodoros { get; set; }
    public int FocusMinutes { get; set; }
}

public class ChartBar
{
    public string Label { get; set; } = "";
    // Week and year label every bar; a 30-day chart only every seventh
    public bool ShowLabel { get; set; }
    public string Tooltip { get; set; } = "";
    public int Pomodoros { get; set; }
    public int FocusMinutes { get; set; }
    public bool IsCurrent { get; set; }
}

public class TaskStats
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
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
    /// <param name="utcOffset">The viewer's offset from UTC; days, weeks and months start at their local midnight.</param>
    Task<StatsViewModel> GetStatsAsync(string userId, TimeSpan utcOffset, StatsRange range = StatsRange.Week);
}

public class StatsService : IStatsService
{
    private const int HeatmapWeeks = 53;
    private const int TopTaskCount = 5;

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public StatsService(IDbContextFactory<ApplicationDbContext> factory) => _factory = factory;

    public async Task<StatsViewModel> GetStatsAsync(string userId, TimeSpan utcOffset, StatsRange range = StatsRange.Week)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow + utcOffset);
        var rangeStart = range switch
        {
            StatsRange.Month => today.AddDays(-29),
            StatsRange.Year => new DateOnly(today.Year, today.Month, 1).AddMonths(-11),
            _ => today.AddDays(-6)
        };
        var rangeDays = today.DayNumber - rangeStart.DayNumber + 1;
        var previousStart = rangeStart.AddDays(-rangeDays);
        var heatmapStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).AddDays(-7 * (HeatmapWeeks - 1)); // weeks start on Monday

        // Sessions are stored in UTC. Kind must stay Utc for PostgreSQL's timestamptz parameters.
        DateTime ToUtc(DateOnly localDay) =>
            DateTime.SpecifyKind(localDay.ToDateTime(TimeOnly.MinValue) - utcOffset, DateTimeKind.Utc);

        var baseQuery = db.PomodoroSessions
            .Where(s => s.UserId == userId && s.SessionType == TimerMode.Pomodoro && s.CompletedAt.HasValue);

        var totalPomodoros    = await baseQuery.CountAsync();
        var totalFocusMinutes = await baseQuery.SumAsync(s => (int?)s.DurationSeconds) / 60 ?? 0;

        // Everything else comes from one window: the heatmap's year, or the range and the period
        // before it if that reaches further back (two years for the 12-month view)
        var since = previousStart < heatmapStart ? previousStart : heatmapStart;
        var sinceUtc = ToUtc(since);
        var sessions = (await baseQuery
                .Where(s => s.CompletedAt!.Value >= sinceUtc)
                .Select(s => new { s.CompletedAt, s.StartedAt, s.DurationSeconds, s.TaskId })
                .ToListAsync())
            .Select(s => new
            {
                Day = DateOnly.FromDateTime(s.CompletedAt!.Value + utcOffset),
                StartHour = (s.StartedAt + utcOffset).Hour,
                s.DurationSeconds,
                s.TaskId
            })
            .ToList();

        var byDay = sessions
            .GroupBy(s => s.Day)
            .ToDictionary(g => g.Key, g => new DailyStats
            {
                Date = g.Key,
                Pomodoros = g.Count(),
                FocusMinutes = g.Sum(s => s.DurationSeconds) / 60
            });
        DailyStats Day(DateOnly date) => byDay.TryGetValue(date, out var d) ? d : new DailyStats { Date = date };

        // Streaks
        var streakDay = byDay.ContainsKey(today) ? today : today.AddDays(-1);
        var currentStreak = 0;
        while (streakDay >= since && byDay.ContainsKey(streakDay))
        {
            currentStreak++;
            streakDay = streakDay.AddDays(-1);
        }

        var heatmap = new List<int>();
        int bestStreak = 0, run = 0, activeDays = 0;
        for (var d = heatmapStart; d <= today; d = d.AddDays(1))
        {
            var count = Day(d).Pomodoros;
            heatmap.Add(count);
            run = count > 0 ? run + 1 : 0;
            bestStreak = Math.Max(bestStreak, run);
            if (count > 0) activeDays++;
        }

        // The selected range
        var inRange = sessions.Where(s => s.Day >= rangeStart).ToList();
        var hours = new int[24];
        foreach (var s in inRange) hours[s.StartHour]++;

        var bars = new List<ChartBar>();
        if (range == StatsRange.Year)
        {
            for (var month = rangeStart; month <= today; month = month.AddMonths(1))
            {
                var inMonth = inRange.Where(s => s.Day.Year == month.Year && s.Day.Month == month.Month).ToList();
                var focus = inMonth.Sum(s => s.DurationSeconds) / 60;
                bars.Add(new ChartBar
                {
                    Label = month.ToString("MMM", Culture)[..1],
                    ShowLabel = true,
                    Tooltip = $"{month.ToString("MMMM yyyy", Culture)}: {Pomodoros(inMonth.Count)}, {FormatMinutes(focus)}",
                    Pomodoros = inMonth.Count,
                    FocusMinutes = focus,
                    IsCurrent = month.Year == today.Year && month.Month == today.Month
                });
            }
        }
        else
        {
            for (var d = rangeStart; d <= today; d = d.AddDays(1))
            {
                var day = Day(d);
                bars.Add(new ChartBar
                {
                    Label = range == StatsRange.Week ? d.ToString("ddd", Culture) : d.Day.ToString(Culture),
                    ShowLabel = range == StatsRange.Week || (today.DayNumber - d.DayNumber) % 7 == 0,
                    Tooltip = $"{d.ToString("ddd d MMM", Culture)}: {Pomodoros(day.Pomodoros)}, {FormatMinutes(day.FocusMinutes)}",
                    Pomodoros = day.Pomodoros,
                    FocusMinutes = day.FocusMinutes,
                    IsCurrent = d == today
                });
            }
        }

        var bestDay = byDay.Values
            .Where(d => d.Date >= rangeStart)
            .OrderByDescending(d => d.Pomodoros)
            .ThenByDescending(d => d.Date)
            .FirstOrDefault();

        // Top tasks: aggregate here, then fetch just their titles (no join; see copilot-instructions)
        var taskTotals = inRange
            .Where(s => s.TaskId.HasValue)
            .GroupBy(s => s.TaskId!.Value)
            .Select(g => new { TaskId = g.Key, Pomodoros = g.Count(), FocusMinutes = g.Sum(s => s.DurationSeconds) / 60 })
            .OrderByDescending(t => t.Pomodoros)
            .ThenByDescending(t => t.FocusMinutes)
            .Take(TopTaskCount)
            .ToList();
        var topIds = taskTotals.Select(t => t.TaskId).ToList();
        var topInfo = topIds.Count > 0
            ? await db.PomodoroTasks
                .Where(t => topIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => new { t.Title, t.IsCompleted })
            : new();
        var topTasks = taskTotals
            .Where(t => topInfo.ContainsKey(t.TaskId))
            .Select(t => new TaskStats
            {
                Title = topInfo[t.TaskId].Title,
                IsCompleted = topInfo[t.TaskId].IsCompleted,
                Pomodoros = t.Pomodoros,
                FocusMinutes = t.FocusMinutes
            })
            .ToList();

        // Task stats
        var tasksCompleted = await db.PomodoroTasks.CountAsync(t => t.UserId == userId && t.IsCompleted);
        var tasksTotal     = await db.PomodoroTasks.CountAsync(t => t.UserId == userId);

        // Ticked off without a pomodoro means it wasn't timed, so it says nothing about the estimate
        var estimates = await db.PomodoroTasks
            .Where(t => t.UserId == userId && t.IsCompleted && t.CompletedPomodoros > 0 && t.EstimatedPomodoros > 0)
            .GroupBy(t => 1)
            .Select(g => new { Spent = g.Sum(t => t.CompletedPomodoros), Estimated = g.Sum(t => t.EstimatedPomodoros) })
            .FirstOrDefaultAsync();

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

        var todayStats = Day(today);
        return new StatsViewModel
        {
            Range                  = range,
            TodayPomodoros         = todayStats.Pomodoros,
            TodayFocusMinutes      = todayStats.FocusMinutes,
            TotalPomodoros         = totalPomodoros,
            TotalFocusMinutes      = totalFocusMinutes,
            CurrentStreak          = currentStreak,
            BestStreak             = bestStreak,
            TasksCompleted         = tasksCompleted,
            TasksTotal             = tasksTotal,
            EstimateRatio          = estimates is null ? null : (double)estimates.Spent / estimates.Estimated,
            RangePomodoros         = inRange.Count,
            RangeFocusMinutes      = inRange.Sum(s => s.DurationSeconds) / 60,
            PreviousRangePomodoros = sessions.Count(s => s.Day >= previousStart && s.Day < rangeStart),
            RangeDays              = rangeDays,
            BestDay                = bestDay,
            Bars                   = bars,
            Hours                  = hours,
            TopTasks               = topTasks,
            PomodorosWithoutTask   = inRange.Count(s => !s.TaskId.HasValue),
            HeatmapStart           = heatmapStart,
            Heatmap                = heatmap,
            ActiveDays             = activeDays,
            RecentSessions         = recentSessions
        };
    }

    private static string Pomodoros(int count) => count == 1 ? "1 pomodoro" : $"{count} pomodoros";

    public static string FormatMinutes(int totalMinutes)
    {
        if (totalMinutes < 60) return $"{totalMinutes}m";
        var h = totalMinutes / 60;
        var m = totalMinutes % 60;
        return m == 0 ? $"{h}h" : $"{h}h {m}m";
    }
}
