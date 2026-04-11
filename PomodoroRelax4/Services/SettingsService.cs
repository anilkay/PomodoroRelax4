using Microsoft.EntityFrameworkCore;
using PomodoroRelax4.Data;
using PomodoroRelax4.Models;

namespace PomodoroRelax4.Services;

public interface ISettingsService
{
    Task<UserSettings> GetSettingsAsync(string userId);
    Task SaveSettingsAsync(UserSettings settings);
    Task EnsureDefaultSettingsAsync(string userId);
}

public class SettingsService : ISettingsService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public SettingsService(IDbContextFactory<ApplicationDbContext> factory) => _factory = factory;

    public async Task<UserSettings> GetSettingsAsync(string userId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var settings = await db.UserSettings.FindAsync(userId);
        if (settings is null)
        {
            settings = new UserSettings { UserId = userId };
            db.UserSettings.Add(settings);
            await db.SaveChangesAsync();
        }
        return settings;
    }

    public async Task SaveSettingsAsync(UserSettings settings)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.UserSettings.Update(settings);
        await db.SaveChangesAsync();
    }

    public async Task EnsureDefaultSettingsAsync(string userId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        if (!await db.UserSettings.AnyAsync(s => s.UserId == userId))
        {
            db.UserSettings.Add(new UserSettings { UserId = userId });
            await db.SaveChangesAsync();
        }
    }
}
