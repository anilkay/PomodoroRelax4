using PomodoroRelax4.Models;

namespace PomodoroRelax4.Services;

public interface ITaskService
{
    Task<List<PomodoroTask>> GetTasksAsync(string userId);
    Task<PomodoroTask> AddTaskAsync(string userId, string title, int estimatedPomodoros,
        TaskPriority priority = TaskPriority.Normal, DateOnly? dueDate = null, string? notes = null);
    Task UpdateTaskAsync(PomodoroTask task);
    Task DeleteTaskAsync(int taskId, string userId);
    /// <summary>Deletes the user's finished tasks; their sessions stay, without a task.</summary>
    Task<int> DeleteCompletedTasksAsync(string userId);
    Task ReorderTasksAsync(string userId, List<int> orderedIds);
    Task IncrementPomodoroAsync(int taskId, string userId);
}
