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

    public async Task<PomodoroTask> AddTaskAsync(string userId, string title, int estimatedPomodoros)
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
            SortOrder = maxOrder + 1
        };

        db.PomodoroTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    public async Task UpdateTaskAsync(PomodoroTask task)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.PomodoroTasks.Update(task);
        await db.SaveChangesAsync();
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

    public async Task IncrementPomodoroAsync(int taskId, string userId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var task = await db.PomodoroTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId);

        if (task is not null)
        {
            task.CompletedPomodoros++;
            await db.SaveChangesAsync();
        }
    }
}
