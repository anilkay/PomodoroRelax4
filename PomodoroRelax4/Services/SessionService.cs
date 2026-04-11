using Microsoft.EntityFrameworkCore;
using PomodoroRelax4.Data;
using PomodoroRelax4.Models;

namespace PomodoroRelax4.Services;

public interface ISessionService
{
    Task RecordSessionAsync(string userId, int? taskId, TimerMode mode, int durationSeconds);
}

public class SessionService : ISessionService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public SessionService(IDbContextFactory<ApplicationDbContext> factory) => _factory = factory;

    public async Task RecordSessionAsync(string userId, int? taskId, TimerMode mode, int durationSeconds)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var session = new PomodoroSession
        {
            UserId = userId,
            TaskId = taskId,
            SessionType = mode,
            DurationSeconds = durationSeconds,
            StartedAt = DateTime.UtcNow.AddSeconds(-durationSeconds),
            CompletedAt = DateTime.UtcNow
        };

        db.PomodoroSessions.Add(session);
        await db.SaveChangesAsync();
    }
}
