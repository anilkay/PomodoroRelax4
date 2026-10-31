namespace PomodoroRelax4.Services;

/// <summary>Password policy shared by Identity (Program.cs) and the forms that set a password.</summary>
public static class PasswordRules
{
    // Length matters more than character classes; Identity's other complexity rules stay off.
    public const int MinLength = 12;
}
