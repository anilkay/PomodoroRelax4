using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace PomodoroRelax4.Pages.Account;

[EnableRateLimiting("login")]
public class LoginModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserManager<IdentityUser> _userManager;

    public LoginModel(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [BindProperty]
    public LoginInputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet(string? returnUrl = null, string? error = null)
    {
        ReturnUrl = returnUrl ?? "/";
        if (error == "ratelimit")
            ErrorMessage = "Too many sign-in attempts from your network. Try again in 15 minutes.";
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        // LocalRedirect throws on anything else; an off-site returnUrl just goes home
        if (string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl))
            returnUrl = "/";

        if (!ModelState.IsValid)
            return Page();

        // Support login by email or username
        var user = await _userManager.FindByEmailAsync(Input.EmailOrUserName)
                   ?? await _userManager.FindByNameAsync(Input.EmailOrUserName);

        if (user is null)
        {
            ErrorMessage = "Invalid username/email or password.";
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(user, Input.Password, isPersistent: true, lockoutOnFailure: true);
        if (result.Succeeded)
            return LocalRedirect(returnUrl);

        if (result.IsLockedOut)
        {
            ErrorMessage = "Account locked out due to multiple failed attempts. Try again in 15 minutes.";
            return Page();
        }

        ErrorMessage = "Invalid username/email or password.";
        return Page();
    }

    public class LoginInputModel
    {
        [Required]
        public string EmailOrUserName { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }
}
