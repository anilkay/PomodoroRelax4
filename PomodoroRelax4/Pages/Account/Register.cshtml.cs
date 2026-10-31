using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using PomodoroRelax4.Services;
using System.ComponentModel.DataAnnotations;

namespace PomodoroRelax4.Pages.Account;

[EnableRateLimiting("register")]
public class RegisterModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly ISettingsService _settingsService;
    private readonly IAdminAccountService _adminAccount;

    public RegisterModel(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        ISettingsService settingsService,
        IAdminAccountService adminAccount)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _settingsService = settingsService;
        _adminAccount = adminAccount;
    }

    [BindProperty]
    public RegisterInputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    // Public sign-up is closed; this page only creates the first admin account.
    public bool RegistrationOpen { get; set; }

    public async Task<IActionResult> OnGetAsync(string? error = null)
    {
        RegistrationOpen = await _adminAccount.IsRegistrationOpenAsync();
        if (error == "ratelimit")
            ErrorMessage = "Too many registration attempts. Please try again in an hour.";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        RegistrationOpen = await _adminAccount.IsRegistrationOpenAsync();
        if (!RegistrationOpen)
            return Page();

        if (!ModelState.IsValid)
            return Page();

        if (!_adminAccount.IsAdminEmail(Input.Email))
        {
            ErrorMessage = "Registration is closed. Ask the administrator for an account.";
            return Page();
        }

        if (Input.Password != Input.ConfirmPassword)
        {
            ErrorMessage = "Passwords do not match.";
            return Page();
        }

        var user = new IdentityUser
        {
            UserName = Input.UserName,
            Email = Input.Email
        };

        var result = await _userManager.CreateAsync(user, Input.Password);
        if (result.Succeeded)
        {
            await _settingsService.EnsureDefaultSettingsAsync(user.Id);
            // Before sign-in, so the cookie already carries the Admin role.
            await _adminAccount.SyncAdminRoleAsync();
            await _signInManager.SignInAsync(user, isPersistent: true);
            return LocalRedirect("/");
        }

        ErrorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
        return Page();
    }

    public class RegisterInputModel
    {
        [Required, MaxLength(50)]
        public string UserName { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(PasswordRules.MinLength)]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
