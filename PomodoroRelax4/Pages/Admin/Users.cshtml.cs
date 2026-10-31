using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PomodoroRelax4.Services;
using System.ComponentModel.DataAnnotations;

namespace PomodoroRelax4.Pages.Admin;

[Authorize(Roles = AdminAccountService.AdminRole)]
public class UsersModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ISettingsService _settingsService;
    private readonly IAdminAccountService _adminAccount;

    public UsersModel(
        UserManager<IdentityUser> userManager,
        ISettingsService settingsService,
        IAdminAccountService adminAccount)
    {
        _userManager = userManager;
        _settingsService = settingsService;
        _adminAccount = adminAccount;
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

    private async Task LoadUsersAsync()
    {
        var users = await _userManager.Users
            .OrderBy(u => u.UserName)
            .Select(u => new { u.UserName, u.Email })
            .ToListAsync();

        Users = users
            .Select(u => new UserRow(u.UserName ?? "", u.Email ?? "", _adminAccount.IsAdminEmail(u.Email)))
            .ToList();
    }

    public record UserRow(string UserName, string Email, bool IsAdmin);

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
