using System.ComponentModel.DataAnnotations;

namespace PomodoroRelax4.Models;

public class PomodoroTask
{
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
}
