using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PomodoroRelax4.Data;
using PomodoroRelax4.Services;
using System.ComponentModel.DataAnnotations;

namespace PomodoroRelax4.Pages.Admin;

[Authorize(Roles = AdminAccountService.AdminRole)]
public class UsersModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ISettingsService _settingsService;
    private readonly IAdminAccountService _adminAccount;
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public UsersModel(
        UserManager<IdentityUser> userManager,
        ISettingsService settingsService,
        IAdminAccountService adminAccount,
        IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _userManager = userManager;
        _settingsService = settingsService;
        _adminAccount = adminAccount;
        _dbFactory = dbFactory;
    }

    [BindProperty]
    public NewUserInputModel Input { get; set; } = new();

    public List<UserRow> Users { get; set; } = new();

    public string? ErrorMessage { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync() => await LoadUsersAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadUsersAsync();
            return Page();
        }

        var user = new IdentityUser
        {
            UserName = Input.UserName.Trim(),
            Email = Input.Email.Trim()
        };

        var result = await _userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            ErrorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
            await LoadUsersAsync();
            return Page();
        }

        await _settingsService.EnsureDefaultSettingsAsync(user.Id);
        // Creating the configured admin e-mail here hands it the Admin role.
        await _adminAccount.SyncAdminRoleAsync();

        StatusMessage = $"User \"{user.UserName}\" created. Share the password with them over a private channel.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(string id, string newPassword)
    {
        // Input is bound on every post; keep the empty Add User form free of errors.
        ModelState.Clear();
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return RedirectToPage();

        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
        {
            ErrorMessage = "The new password must be at least 6 characters.";
            await LoadUsersAsync();
            return Page();
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            ErrorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
            await LoadUsersAsync();
            return Page();
        }

        // A forgotten password usually comes with a lockout; clear it too.
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);

        StatusMessage = $"Password for \"{user.UserName}\" reset. Their other sessions are signed out.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        ModelState.Clear();
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return RedirectToPage();

        // The admin account follows Admin:Email; change the config instead of deleting it here.
        if (_adminAccount.IsAdminEmail(user.Email))
        {
            ErrorMessage = "The admin account cannot be deleted.";
            await LoadUsersAsync();
            return Page();
        }

        // App data references users by id only, without a foreign key, so remove it explicitly.
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            await db.PomodoroSessions.Where(s => s.UserId == user.Id).ExecuteDeleteAsync();
            await db.PomodoroTasks.Where(t => t.UserId == user.Id).ExecuteDeleteAsync();
            await db.UserSettings.Where(s => s.UserId == user.Id).ExecuteDeleteAsync();
        }

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            ErrorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
            await LoadUsersAsync();
            return Page();
        }

        StatusMessage = $"User \"{user.UserName}\" and their data deleted.";
        return RedirectToPage();
    }

    private async Task LoadUsersAsync()
    {
        var users = await _userManager.Users
            .OrderBy(u => u.UserName)
            .Select(u => new { u.Id, u.UserName, u.Email, u.LockoutEnd })
            .ToListAsync();

        var now = DateTimeOffset.UtcNow;
        Users = users
            .Select(u => new UserRow(
                u.Id,
                u.UserName ?? "",
                u.Email ?? "",
                _adminAccount.IsAdminEmail(u.Email),
                u.LockoutEnd > now))
            .ToList();
    }

    public record UserRow(string Id, string UserName, string Email, bool IsAdmin, bool IsLockedOut);

    public class NewUserInputModel
    {
        [Required, MaxLength(50)]
        public string UserName { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;
    }
}
