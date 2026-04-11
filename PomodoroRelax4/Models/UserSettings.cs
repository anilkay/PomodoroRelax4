using System.ComponentModel.DataAnnotations;

namespace PomodoroRelax4.Models;

public class UserSettings
{
    [Key]
    public string UserId { get; set; } = null!;

    public int PomodoroDurationMinutes { get; set; } = 25;

    public int ShortBreakDurationMinutes { get; set; } = 5;

    public int LongBreakDurationMinutes { get; set; } = 15;
}
