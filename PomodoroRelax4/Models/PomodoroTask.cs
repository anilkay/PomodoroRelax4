using System.ComponentModel.DataAnnotations;

namespace PomodoroRelax4.Models;

public class PomodoroTask
{
    public const int MaxNotesLength = 2000;

    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public int EstimatedPomodoros { get; set; } = 1;

    public int CompletedPomodoros { get; set; }

    public bool IsCompleted { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    // A calendar day in the user's own time zone, so no UTC conversion
    public DateOnly? DueDate { get; set; }

    [MaxLength(MaxNotesLength)]
    public string? Notes { get; set; }
}

// Stored as numbers; keep the values when adding one
public enum TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}
