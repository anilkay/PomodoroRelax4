using Microsoft.EntityFrameworkCore;
using PomodoroRelax4.Data;
using PomodoroRelax4.Models;

namespace PomodoroRelax4.Services;

public class TaskService : ITaskService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public TaskService(IDbContextFactory<ApplicationDbContext> factory) => _factory = factory;

    public async Task<List<PomodoroTask>> GetTasksAsync(string userId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.PomodoroTasks
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.SortOrder)
            .ToListAsync();
    }

    public async Task<PomodoroTask> AddTaskAsync(string userId, string title, int estimatedPomodoros,
        TaskPriority priority = TaskPriority.Normal, DateOnly? dueDate = null, string? notes = null)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var maxOrder = await db.PomodoroTasks
            .Where(t => t.UserId == userId)
            .MaxAsync(t => (int?)t.SortOrder) ?? 0;

        var task = new PomodoroTask
        {
            UserId = userId,
            Title = title,
            EstimatedPomodoros = estimatedPomodoros,
            SortOrder = maxOrder + 1,
            Priority = Enum.IsDefined(priority) ? priority : TaskPriority.Normal,
            DueDate = dueDate,
            Notes = CleanNotes(notes)
        };

        db.PomodoroTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    public async Task UpdateTaskAsync(PomodoroTask task)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var stored = await db.PomodoroTasks
            .FirstOrDefaultAsync(t => t.Id == task.Id && t.UserId == task.UserId);
        if (stored is null) return;

        // Copy only what the UI edits: the caller's copy may hold a stale pomodoro count
        // or sort order (another tab, a timer that just finished).
        stored.Title = task.Title;
        stored.IsCompleted = task.IsCompleted;
        stored.EstimatedPomodoros = task.EstimatedPomodoros;
        stored.Priority = Enum.IsDefined(task.Priority) ? task.Priority : TaskPriority.Normal;
        stored.DueDate = task.DueDate;
        stored.Notes = CleanNotes(task.Notes);
        await db.SaveChangesAsync();
    }

    public async Task<int> DeleteCompletedTasksAsync(string userId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        // The database sets the sessions' TaskId to null (OnDelete SetNull), so stats keep them
        return await db.PomodoroTasks
            .Where(t => t.UserId == userId && t.IsCompleted)
            .ExecuteDeleteAsync();
    }

    // Blank notes are stored as null; longer ones are cut to the column size
    private static string? CleanNotes(string? notes)
    {
        notes = notes?.Trim();
        if (string.IsNullOrEmpty(notes)) return null;
        return notes.Length > PomodoroTask.MaxNotesLength ? notes[..PomodoroTask.MaxNotesLength] : notes;
    }

    public async Task DeleteTaskAsync(int taskId, string userId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var task = await db.PomodoroTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId);

        if (task is not null)
        {
            db.PomodoroTasks.Remove(task);
            await db.SaveChangesAsync();
        }
    }

    public async Task ReorderTasksAsync(string userId, List<int> orderedIds)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var tasks = await db.PomodoroTasks
            .Where(t => t.UserId == userId)
            .ToListAsync();

        for (int i = 0; i < orderedIds.Count; i++)
        {
            var task = tasks.FirstOrDefault(t => t.Id == orderedIds[i]);
            if (task is not null)
                task.SortOrder = i;
        }

        await db.SaveChangesAsync();
    }

    public async Task<PomodoroTask?> IncrementPomodoroAsync(int taskId, string userId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var task = await db.PomodoroTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId);

        if (task is not null)
        {
            task.CompletedPomodoros++;
            await db.SaveChangesAsync();
        }
        return task;
    }
}
