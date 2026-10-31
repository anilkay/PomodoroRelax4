using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PomodoroRelax4.Data;
using PomodoroRelax4.Models;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PomodoroRelax4.Services;

/// <summary>Everything stored for one user, as offered on Settings → Export.</summary>
public record UserDataExport(
    DateTime ExportedAtUtc,
    ExportedAccount Account,
    ExportedSettings Settings,
    List<ExportedTask> Tasks,
    List<ExportedSession> Sessions);

public record ExportedAccount(string UserName, string Email);

public record ExportedSettings(int PomodoroMinutes, int ShortBreakMinutes, int LongBreakMinutes);

public record ExportedTask(
    int Id, string Title, int EstimatedPomodoros, int CompletedPomodoros,
    bool IsCompleted, int SortOrder, DateTime CreatedAtUtc);

public record ExportedSession(
    TimerMode Type, int DurationSeconds, DateTime StartedAtUtc, DateTime? CompletedAtUtc,
    int? TaskId, string? TaskTitle);

public interface IExportService
{
    Task<UserDataExport?> GetUserDataAsync(string userId);
    byte[] ToJson(UserDataExport data);
    byte[] ToExcel(UserDataExport data);
}

public class ExportService : IExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        // Keep "ı", "ş", "+" readable; the file is a download, never embedded in a page
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly UserManager<IdentityUser> _userManager;

    public ExportService(IDbContextFactory<ApplicationDbContext> factory, UserManager<IdentityUser> userManager)
    {
        _factory = factory;
        _userManager = userManager;
    }

    public async Task<UserDataExport?> GetUserDataAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return null;

        await using var db = await _factory.CreateDbContextAsync();

        var settings = await db.UserSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId)
                       ?? new UserSettings { UserId = userId };

        var tasks = await db.PomodoroTasks.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.SortOrder)
            .Select(t => new ExportedTask(t.Id, t.Title, t.EstimatedPomodoros, t.CompletedPomodoros,
                t.IsCompleted, t.SortOrder, t.CreatedAt))
            .ToListAsync();

        // Task titles joined in memory (see StatsService for why not in the query)
        var titles = tasks.ToDictionary(t => t.Id, t => t.Title);
        var sessions = (await db.PomodoroSessions.AsNoTracking()
                .Where(s => s.UserId == userId)
                .OrderBy(s => s.StartedAt)
                .ToListAsync())
            .Select(s => new ExportedSession(s.SessionType, s.DurationSeconds, s.StartedAt, s.CompletedAt,
                s.TaskId, s.TaskId is int id && titles.TryGetValue(id, out var title) ? title : null))
            .ToList();

        return new UserDataExport(
            DateTime.UtcNow,
            new ExportedAccount(user.UserName ?? "", user.Email ?? ""),
            new ExportedSettings(settings.PomodoroDurationMinutes, settings.ShortBreakDurationMinutes,
                settings.LongBreakDurationMinutes),
            tasks,
            sessions);
    }

    public byte[] ToJson(UserDataExport data) => JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);

    public byte[] ToExcel(UserDataExport data)
    {
        using var workbook = new XLWorkbook();

        // SetValue with a string always stores text, so a title like "=HYPERLINK(...)" is never run as a formula
        var tasks = workbook.AddWorksheet("Tasks");
        WriteHeader(tasks, "Order", "Title", "Estimated", "Completed pomodoros", "Done", "Created (UTC)");
        var row = 2;
        foreach (var t in data.Tasks)
        {
            tasks.Cell(row, 1).SetValue(t.SortOrder);
            tasks.Cell(row, 2).SetValue(t.Title);
            tasks.Cell(row, 3).SetValue(t.EstimatedPomodoros);
            tasks.Cell(row, 4).SetValue(t.CompletedPomodoros);
            tasks.Cell(row, 5).SetValue(t.IsCompleted ? "Yes" : "No");
            tasks.Cell(row, 6).SetValue(t.CreatedAtUtc);
            row++;
        }
        tasks.Column(6).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";

        var sessions = workbook.AddWorksheet("Sessions");
        WriteHeader(sessions, "Type", "Minutes", "Started (UTC)", "Completed (UTC)", "Task");
        row = 2;
        foreach (var s in data.Sessions)
        {
            sessions.Cell(row, 1).SetValue(s.Type.ToString());
            sessions.Cell(row, 2).SetValue(Math.Round(s.DurationSeconds / 60.0, 1));
            sessions.Cell(row, 3).SetValue(s.StartedAtUtc);
            if (s.CompletedAtUtc is DateTime completed)
                sessions.Cell(row, 4).SetValue(completed);
            sessions.Cell(row, 5).SetValue(s.TaskTitle ?? "");
            row++;
        }
        sessions.Columns(3, 4).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";

        var settings = workbook.AddWorksheet("Settings");
        WriteHeader(settings, "Setting", "Value");
        settings.Cell(2, 1).SetValue("Pomodoro (min)");
        settings.Cell(2, 2).SetValue(data.Settings.PomodoroMinutes);
        settings.Cell(3, 1).SetValue("Short break (min)");
        settings.Cell(3, 2).SetValue(data.Settings.ShortBreakMinutes);
        settings.Cell(4, 1).SetValue("Long break (min)");
        settings.Cell(4, 2).SetValue(data.Settings.LongBreakMinutes);
        settings.Cell(6, 1).SetValue("User");
        settings.Cell(6, 2).SetValue(data.Account.UserName);
        settings.Cell(7, 1).SetValue("Email");
        settings.Cell(7, 2).SetValue(data.Account.Email);
        settings.Cell(8, 1).SetValue("Exported (UTC)");
        settings.Cell(8, 2).SetValue(data.ExportedAtUtc);
        settings.Cell(8, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";

        foreach (var sheet in workbook.Worksheets)
            sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteHeader(IXLWorksheet sheet, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++)
            sheet.Cell(1, i + 1).SetValue(titles[i]);
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
    }
}
