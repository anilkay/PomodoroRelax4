namespace PomodoroRelax4.Models;

public class PomodoroSession
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    public int? TaskId { get; set; }

    public TimerMode SessionType { get; set; }

    public int DurationSeconds { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public PomodoroTask? Task { get; set; }
}
