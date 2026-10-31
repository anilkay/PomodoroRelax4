using Microsoft.AspNetCore.Identity;

namespace PomodoroRelax4.Services;

public interface IAdminAccountService
{
    /// <summary>The admin e-mail from configuration (<c>Admin:Email</c>), or null when unset.</summary>
    string? AdminEmail { get; }

    bool IsAdminEmail(string? email);

    /// <summary>
    /// Self-registration is closed to the public. It is open only until the configured
    /// admin e-mail has an account, and then only for that e-mail.
    /// </summary>
    Task<bool> IsRegistrationOpenAsync();

    /// <summary>Creates the Admin role and makes the configured e-mail its only member.</summary>
    Task SyncAdminRoleAsync();
}

public class AdminAccountService : IAdminAccountService
{
    public const string AdminRole = "Admin";

    private readonly UserManager<IdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public AdminAccountService(
        UserManager<IdentityUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        var email = configuration["Admin:Email"]?.Trim();
        AdminEmail = string.IsNullOrEmpty(email) ? null : email;
    }

    public string? AdminEmail { get; }

    public bool IsAdminEmail(string? email)
        => AdminEmail is not null && string.Equals(email?.Trim(), AdminEmail, StringComparison.OrdinalIgnoreCase);

    public async Task<bool> IsRegistrationOpenAsync()
        => AdminEmail is not null && await _userManager.FindByEmailAsync(AdminEmail) is null;

    public async Task SyncAdminRoleAsync()
    {
        if (!await _roleManager.RoleExistsAsync(AdminRole))
            await _roleManager.CreateAsync(new IdentityRole(AdminRole));

        // Config is the single source of truth: a changed or removed Admin:Email
        // takes the role away from the previous admin.
        foreach (var user in await _userManager.GetUsersInRoleAsync(AdminRole))
        {
            if (!IsAdminEmail(user.Email))
                await _userManager.RemoveFromRoleAsync(user, AdminRole);
        }

        if (AdminEmail is null) return;

        var admin = await _userManager.FindByEmailAsync(AdminEmail);
        if (admin is not null && !await _userManager.IsInRoleAsync(admin, AdminRole))
            await _userManager.AddToRoleAsync(admin, AdminRole);
    }
}
