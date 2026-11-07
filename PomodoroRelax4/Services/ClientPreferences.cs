namespace PomodoroRelax4.Services;

/// <summary>
/// Browser-side values that js/site.js keeps in cookies so the server can use them while it
/// prerenders a page, before the Blazor circuit (and JS interop) is up.
/// </summary>
public static class ClientPreferences
{
    public const string UtcOffsetCookie = "tz";
    public const string HideDoneTasksCookie = "hideDone";

    /// <summary>Minutes ahead of UTC (Istanbul: 180), or null before site.js has set the cookie.</summary>
    public static int? UtcOffsetMinutes(HttpContext? context)
        => context?.Request.Cookies[UtcOffsetCookie] is { } value
           && int.TryParse(value, out var minutes)
           && minutes is >= -14 * 60 and <= 14 * 60
            ? minutes
            : null;

    public static bool HideDoneTasks(HttpContext? context)
        => context?.Request.Cookies[HideDoneTasksCookie] == "1";

    public const string TaskFilterCookie = "taskFilter";

    /// <summary>The list shown under the timer: "today", "important", or null for all tasks.</summary>
    public static string? TaskFilter(HttpContext? context)
        => context?.Request.Cookies[TaskFilterCookie] is "today" or "important" ? context.Request.Cookies[TaskFilterCookie] : null;
}
