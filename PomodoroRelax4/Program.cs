using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PomodoroRelax4.Data;
using PomodoroRelax4.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Database — use factory so Blazor Server services each get their own short-lived context.
// Database:Provider picks SqlServer (default) or PostgreSql; each has its own migrations.
var dbProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (dbProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContextFactory<PostgresDbContext>(options => options.UseNpgsql(connectionString));
    // Services and Identity ask for ApplicationDbContext; hand them the PostgreSQL subclass
    builder.Services.AddSingleton<IDbContextFactory<ApplicationDbContext>>(sp =>
        new DerivedDbContextFactory<PostgresDbContext>(sp.GetRequiredService<IDbContextFactory<PostgresDbContext>>()));
    builder.Services.AddScoped<ApplicationDbContext>(sp => sp.GetRequiredService<PostgresDbContext>());
}
else if (dbProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
}
else
{
    throw new InvalidOperationException($"Unknown Database:Provider '{dbProvider}'. Use SqlServer or PostgreSql.");
}

// Identity
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    options.SignIn.RequireConfirmedAccount = false;
    options.User.RequireUniqueEmail = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/";
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// Services
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IStatsService, StatsService>();
builder.Services.AddScoped<IAdminAccountService, AdminAccountService>();

// GET /healthz — for uptime monitors; checks the database connection
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

// Rate limiting — uses X-Forwarded-For when behind a proxy, falls back to RemoteIpAddress
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("register", context =>
    {
        // Count only form posts: limiting GET as well would reject the ?error=ratelimit page
        // that OnRejected redirects to, looping the browser until it gives up.
        if (!HttpMethods.IsPost(context.Request.Method))
            return RateLimitPartition.GetNoLimiter("get");

        // RemoteIpAddress is already updated by UseForwardedHeaders middleware (called first)
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            });
    });

    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.Redirect("/Account/Register?error=ratelimit");
        await Task.CompletedTask;
    };
});

// Blazor Server + Razor Pages
builder.Services.AddRazorPages();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Apply migrations on startup
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.Migrate();

    // Admin role follows Admin:Email (set it in config, restart to apply)
    await scope.ServiceProvider.GetRequiredService<IAdminAccountService>().SyncAdminRoleAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// ForwardedHeaders must come first so all downstream middleware sees correct IP/scheme
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorPages();
app.MapHealthChecks("/healthz");

app.MapRazorComponents<PomodoroRelax4.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
