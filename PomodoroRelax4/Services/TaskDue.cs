using System.Globalization;
using PomodoroRelax4.Models;

namespace PomodoroRelax4.Services;

// The due-date wording and lists shared by the task list under the timer and the To Do page
public static class TaskDue
{
    /// <summary>The "Today" list: due today, or still open from an earlier day.</summary>
    public static bool IsForToday(PomodoroTask task, DateOnly today)
        => task.DueDate is DateOnly d && (d == today || (d < today && !task.IsCompleted));

    /// <param name="today">The user's own calendar day.</param>
    public static (string Text, bool Overdue)? Label(PomodoroTask task, DateOnly today)
    {
        if (task.DueDate is not DateOnly due) return null;
        var days = due.DayNumber - today.DayNumber;
        var date = due.ToString(due.Year == today.Year ? "d MMM" : "d MMM yyyy", CultureInfo.InvariantCulture);

        if (days < 0)
            return task.IsCompleted ? ($"Due {date}", false) : ($"Overdue · {date}", true);
        return days switch
        {
            0 => ("Due today", false),
            1 => ("Due tomorrow", false),
            < 7 => ($"Due {due.ToString("dddd", CultureInfo.InvariantCulture)}", false),
            _ => ($"Due {date}", false)
        };
    }
}
