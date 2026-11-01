using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PomodoroRelax4.Data;
using PomodoroRelax4.Models;
using System.IO.Compression;
using System.Text.Json;

namespace PomodoroRelax4.Services;

/// <param name="TasksSkipped">Already in the account.</param>
/// <param name="SessionsSkipped">Already in the account.</param>
/// <param name="RowsIgnored">Unusable rows: no title, zero length, a date out of range.</param>
public record ImportResult(int TasksAdded, int TasksSkipped, int SessionsAdded, int SessionsSkipped, int RowsIgnored);

/// <summary>A problem with the uploaded file, worded for the user.</summary>
public class ImportException(string message) : Exception(message);

public interface IImportService
{
    /// <summary>Reads a file made by Settings → Export (.json or .xlsx).</summary>
    Task<UserDataExport> ParseAsync(Stream stream, string fileName);

    /// <summary>
    /// Merges the file into the user's account: adds tasks and sessions it doesn't have yet,
    /// skips ones it does (so the same file can be imported twice), and takes the file's settings.
    /// Never deletes anything.
    /// </summary>
    Task<ImportResult> ImportAsync(string userId, UserDataExport data);
}

public class ImportService : IImportService
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    // An .xlsx is a zip; refuse one that would unpack into something huge before ClosedXML opens it
    private const long MaxUnpackedXlsxBytes = 50 * 1024 * 1024;
    private const int MaxTasks = 10_000;
    private const int MaxSessions = 200_000;
    private const int MaxTitleLength = 200; // PomodoroTask.Title
    private static readonly DateTime EarliestDate = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public ImportService(IDbContextFactory<ApplicationDbContext> factory) => _factory = factory;

    public async Task<UserDataExport> ParseAsync(Stream stream, string fileName)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        if (buffer.Length > MaxFileBytes)
            throw new ImportException("The file is larger than 5 MB.");
        buffer.Position = 0;

        var data = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".json" => ParseJson(buffer),
            ".xlsx" => ParseExcel(buffer),
            _ => throw new ImportException("Choose a .json or .xlsx file exported from PomodoroRelax.")
        };

        if (data.Tasks.Count > MaxTasks || data.Sessions.Count > MaxSessions)
            throw new ImportException($"The file has more than {MaxTasks:N0} tasks or {MaxSessions:N0} sessions.");
        return data;
    }

    private static UserDataExport ParseJson(Stream stream)
    {
        UserDataExport? data;
        try
        {
            data = JsonSerializer.Deserialize<UserDataExport>(stream, ExportService.JsonOptions);
        }
        catch (JsonException)
        {
            data = null;
        }
        if (data is null)
            throw new ImportException("This JSON file isn't a PomodoroRelax export.");

        // Missing arrays deserialize as null
        return data with
        {
            Settings = data.Settings ?? new ExportedSettings(25, 5, 15),
            Tasks = data.Tasks ?? [],
            Sessions = data.Sessions ?? []
        };
    }

    private static UserDataExport ParseExcel(MemoryStream buffer)
    {
        try
        {
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true))
            {
                if (zip.Entries.Sum(e => e.Length) > MaxUnpackedXlsxBytes)
                    throw new ImportException("The Excel file is too large to import.");
            }
            buffer.Position = 0;

            using var workbook = new XLWorkbook(buffer);
            var hasTasks = workbook.TryGetWorksheet("Tasks", out var taskSheet);
            var hasSessions = workbook.TryGetWorksheet("Sessions", out var sessionSheet);
            if (!hasTasks || !hasSessions)
                throw new ImportException("This Excel file isn't a PomodoroRelax export (it needs Tasks and Sessions sheets).");

            workbook.TryGetWorksheet("Settings", out var settingsSheet);
            return new UserDataExport(
                DateTime.UtcNow,
                new ExportedAccount("", ""),
                ReadSettings(settingsSheet),
                ReadTasks(taskSheet),
                ReadSessions(sessionSheet));
        }
        // Anything the zip or OpenXML readers throw on a damaged or foreign file
        catch (Exception ex) when (ex is not ImportException)
        {
            throw new ImportException("The Excel file couldn't be read.");
        }
    }

    private static List<ExportedTask> ReadTasks(IXLWorksheet sheet)
    {
        var columns = Headers(sheet);
        var tasks = new List<ExportedTask>();
        foreach (var row in DataRows(sheet))
        {
            var title = Text(row, columns, "Title");
            if (string.IsNullOrWhiteSpace(title)) continue;

            // Older exports have no Id column; a negative row id still links by title on import
            var id = Number(row, columns, "Id") is double d ? (int)d : -row.RowNumber();
            tasks.Add(new ExportedTask(
                id,
                title,
                (int)(Number(row, columns, "Estimated") ?? 1),
                (int)(Number(row, columns, "Completed pomodoros") ?? 0),
                Text(row, columns, "Done").Equals("Yes", StringComparison.OrdinalIgnoreCase),
                (int)(Number(row, columns, "Order") ?? row.RowNumber()),
                Date(row, columns, "Created (UTC)") ?? DateTime.UtcNow,
                Enum.TryParse<TaskPriority>(Text(row, columns, "Priority"), ignoreCase: true, out var priority)
                    ? priority : TaskPriority.Normal,
                Date(row, columns, "Due") is DateTime due ? DateOnly.FromDateTime(due) : null,
                Text(row, columns, "Notes") is { Length: > 0 } notes ? notes : null));
        }
        return tasks;
    }

    private static List<ExportedSession> ReadSessions(IXLWorksheet sheet)
    {
        var columns = Headers(sheet);
        var sessions = new List<ExportedSession>();
        foreach (var row in DataRows(sheet))
        {
            if (!Enum.TryParse<TimerMode>(Text(row, columns, "Type"), ignoreCase: true, out var type)
                || Date(row, columns, "Started (UTC)") is not DateTime started)
                continue;

            var taskTitle = Text(row, columns, "Task");
            sessions.Add(new ExportedSession(
                type,
                (int)Math.Round((Number(row, columns, "Minutes") ?? 0) * 60),
                started,
                Date(row, columns, "Completed (UTC)"),
                Number(row, columns, "Task id") is double taskId ? (int)taskId : null,
                taskTitle.Length > 0 ? taskTitle : null));
        }
        return sessions;
    }

    private static ExportedSettings ReadSettings(IXLWorksheet? sheet)
    {
        var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (sheet is not null)
        {
            foreach (var row in DataRows(sheet))
            {
                if (row.Cell(2).TryGetValue<double>(out var value))
                    values.TryAdd(row.Cell(1).GetString().Trim(), (int)value);
            }
        }
        return new ExportedSettings(
            values.GetValueOrDefault("Pomodoro (min)", 25),
            values.GetValueOrDefault("Short break (min)", 5),
            values.GetValueOrDefault("Long break (min)", 15));
    }

    // Column numbers by header text, so reordered or extra columns still read
    private static Dictionary<string, int> Headers(IXLWorksheet sheet)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in sheet.Row(1).CellsUsed())
            columns.TryAdd(cell.GetString().Trim(), cell.Address.ColumnNumber);
        return columns;
    }

    private static IEnumerable<IXLRow> DataRows(IXLWorksheet sheet)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= last; r++)
            yield return sheet.Row(r);
    }

    private static string Text(IXLRow row, Dictionary<string, int> columns, string header)
        => columns.TryGetValue(header, out var c) ? row.Cell(c).GetString().Trim() : "";

    private static double? Number(IXLRow row, Dictionary<string, int> columns, string header)
        => columns.TryGetValue(header, out var c) && row.Cell(c).TryGetValue<double>(out var v) ? v : null;

    private static DateTime? Date(IXLRow row, Dictionary<string, int> columns, string header)
    {
        if (!columns.TryGetValue(header, out var c)) return null;
        var cell = row.Cell(c);
        if (cell.DataType == XLDataType.DateTime) return DateTime.SpecifyKind(cell.GetDateTime(), DateTimeKind.Utc);
        if (cell.DataType == XLDataType.Number) return DateTime.SpecifyKind(DateTime.FromOADate(cell.GetDouble()), DateTimeKind.Utc);
        return null;
    }

    public async Task<ImportResult> ImportAsync(string userId, UserDataExport data)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        // Settings: the file wins, within the same limits as the Settings page
        var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
        if (settings is null)
        {
            settings = new UserSettings { UserId = userId };
            db.UserSettings.Add(settings);
        }
        settings.PomodoroDurationMinutes   = Math.Clamp(data.Settings.PomodoroMinutes,   1, 120);
        settings.ShortBreakDurationMinutes = Math.Clamp(data.Settings.ShortBreakMinutes, 1,  60);
        settings.LongBreakDurationMinutes  = Math.Clamp(data.Settings.LongBreakMinutes,  1, 120);

        // Tasks: a task is "already there" when title and creation time (to the second) match
        var existingTasks = await db.PomodoroTasks.Where(t => t.UserId == userId).ToListAsync();
        var tasksByKey = new Dictionary<string, PomodoroTask>();
        foreach (var t in existingTasks)
            tasksByKey.TryAdd(TaskKey(t.Title, ToUtc(t.CreatedAt)), t);

        var nextOrder = existingTasks.Count == 0 ? 0 : existingTasks.Max(t => t.SortOrder) + 1;
        var byFileId = new Dictionary<int, PomodoroTask>();
        var byTitle = new Dictionary<string, PomodoroTask>(); // for Excel files without task ids
        int tasksAdded = 0, tasksSkipped = 0, rowsIgnored = 0;

        foreach (var t in data.Tasks.OrderBy(t => t.SortOrder))
        {
            var title = (t.Title ?? "").Trim();
            if (title.Length == 0) { rowsIgnored++; continue; }
            if (title.Length > MaxTitleLength) title = title[..MaxTitleLength];

            var created = ValidDate(t.CreatedAtUtc) ?? DateTime.UtcNow;
            var key = TaskKey(title, created);
            if (!tasksByKey.TryGetValue(key, out var task))
            {
                task = new PomodoroTask
                {
                    UserId = userId,
                    Title = title,
                    EstimatedPomodoros = Math.Clamp(t.EstimatedPomodoros, 1, 1000),
                    CompletedPomodoros = Math.Clamp(t.CompletedPomodoros, 0, 100_000),
                    IsCompleted = t.IsCompleted,
                    SortOrder = nextOrder++,
                    CreatedAt = created,
                    Priority = Enum.IsDefined(t.Priority) ? t.Priority : TaskPriority.Normal,
                    DueDate = t.DueDate,
                    Notes = t.Notes?.Trim() is { Length: > 0 } notes
                        ? notes[..Math.Min(notes.Length, PomodoroTask.MaxNotesLength)]
                        : null
                };
                db.PomodoroTasks.Add(task);
                tasksByKey[key] = task;
                tasksAdded++;
            }
            else
            {
                tasksSkipped++;
            }

            byFileId.TryAdd(t.Id, task);
            byTitle.TryAdd(title, task);
        }
        await db.SaveChangesAsync(); // new tasks get their ids here, before sessions point at them

        // Sessions: "already there" when type, length and start time (to the second) match
        var existingSessions = await db.PomodoroSessions
            .Where(s => s.UserId == userId)
            .Select(s => new { s.SessionType, s.DurationSeconds, s.StartedAt })
            .ToListAsync();
        var sessionKeys = existingSessions
            .Select(s => SessionKey(s.SessionType, s.DurationSeconds, ToUtc(s.StartedAt)))
            .ToHashSet();
        int sessionsAdded = 0, sessionsSkipped = 0;

        foreach (var s in data.Sessions)
        {
            var started = ValidDate(s.StartedAtUtc);
            if (!Enum.IsDefined(s.Type) || s.DurationSeconds is <= 0 or > 86_400 || started is null)
            {
                rowsIgnored++;
                continue;
            }
            if (!sessionKeys.Add(SessionKey(s.Type, s.DurationSeconds, started.Value)))
            {
                sessionsSkipped++;
                continue;
            }

            var task = s.TaskId is int id && byFileId.TryGetValue(id, out var byId) ? byId
                     : s.TaskTitle is { } title && byTitle.TryGetValue(title.Trim(), out var byName) ? byName
                     : null;
            db.PomodoroSessions.Add(new PomodoroSession
            {
                UserId = userId,
                TaskId = task?.Id,
                SessionType = s.Type,
                DurationSeconds = s.DurationSeconds,
                StartedAt = started.Value,
                CompletedAt = s.CompletedAtUtc is DateTime completed ? ValidDate(completed) : null
            });
            sessionsAdded++;
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return new ImportResult(tasksAdded, tasksSkipped, sessionsAdded, sessionsSkipped, rowsIgnored);
    }

    // SQL Server hands back Unspecified, PostgreSQL Utc; both are stored as UTC
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value
    };

    private static DateTime? ValidDate(DateTime value)
    {
        var utc = ToUtc(value);
        return utc < EarliestDate || utc > DateTime.UtcNow.AddDays(1) ? null : utc;
    }

    // Excel keeps times as fractional days, so compare to the nearest second
    private static DateTime ToSecond(DateTime utc)
        => new((utc.Ticks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    private static string TaskKey(string title, DateTime createdUtc) => $"{title}\u0001{ToSecond(createdUtc):O}";

    private static string SessionKey(TimerMode type, int seconds, DateTime startedUtc)
        => $"{type}\u0001{seconds}\u0001{ToSecond(startedUtc):O}";
}
