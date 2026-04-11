using PomodoroRelax4.Models;

namespace PomodoroRelax4.Services;

public interface ITaskService
{
    Task<List<PomodoroTask>> GetTasksAsync(string userId);
    Task<PomodoroTask> AddTaskAsync(string userId, string title, int estimatedPomodoros);
    Task UpdateTaskAsync(PomodoroTask task);
    Task DeleteTaskAsync(int taskId, string userId);
    Task ReorderTasksAsync(string userId, List<int> orderedIds);
    Task IncrementPomodoroAsync(int taskId, string userId);
}
