using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PomodoroRelax4.Data;
using PomodoroRelax4.Services;
using System.Security.Claims;
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
    options.Password.RequiredLength = PasswordRules.MinLength;
    options.SignIn.RequireConfirmedAccount = false;
    options.User.RequireUniqueEmail = true;
    // The "login" rate limit (per IP) is the main brute-force guard. The account lockout is a
    // backstop for guesses spread over many IPs; set high so one IP cannot lock someone out quickly.
    options.Lockout.MaxFailedAccessAttempts = 20;
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
    // Production is HTTPS-only behind the proxy; local development runs on plain HTTP
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

// How soon a password change, reset or deleted account signs out other browsers (cookies) and
// open Blazor tabs (IdentityRevalidatingAuthenticationStateProvider); the default is 30 minutes.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = IdentityRevalidatingAuthenticationStateProvider.Interval);

if (!builder.Environment.IsDevelopment())
{
    // The app itself only listens on HTTP; the proxy in front terminates TLS on 443
    builder.Services.AddHttpsRedirection(options =>
    {
        options.HttpsPort = 443;
        options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
    });
}

// Services
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IStatsService, StatsService>();
builder.Services.AddScoped<IAdminAccountService, AdminAccountService>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<IImportService, ImportService>();

// GET /healthz — for uptime monitors; checks the database connection
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

// Rate limiting — uses X-Forwarded-For when behind a proxy, falls back to RemoteIpAddress
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("register", context => FormPostsPerIp(context, 5, TimeSpan.FromHours(1)));
    options.AddPolicy("login", context => FormPostsPerIp(context, 10, TimeSpan.FromMinutes(15)));

    // Back to the form, which explains the limit
    options.OnRejected = (context, _) =>
    {
        var page = context.HttpContext.Request.Path.StartsWithSegments("/Account/Login")
            ? "/Account/Login"
            : "/Account/Register";
        context.HttpContext.Response.Redirect($"{page}?error=ratelimit");
        return ValueTask.CompletedTask;
    };

    static RateLimitPartition<string> FormPostsPerIp(HttpContext context, int permitLimit, TimeSpan window)
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
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0
            });
    }
});

// Blazor Server + Razor Pages
builder.Services.AddRazorPages();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
// Registered after AddRazorComponents so it replaces the default, non-revalidating provider
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

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

// ForwardedHeaders must come first so all downstream middleware sees correct IP/scheme
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    // Plain HTTP that reaches the app (Cloudflare without "Always Use HTTPS") is sent to HTTPS.
    // Needs ASPNETCORE_FORWARDEDHEADERS_ENABLED=true behind the proxy, or every request looks
    // like HTTP and redirects forever. /healthz stays on HTTP for checks on the host itself.
    app.UseWhen(context => !context.Request.Path.StartsWithSegments("/healthz"),
        branch => branch.UseHttpsRedirection());
}

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.ContentSecurityPolicy = ContentSecurityPolicy;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseStaticFiles(new StaticFileOptions
{
    // The layouts add ?v=<content hash> (asp-append-version / IFileVersionProvider), so those URLs
    // change whenever the file does and can be cached for good. Anything requested without it
    // (blazor.server.js, the sound) is revalidated each time, so a deploy is never hidden by cache.
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl = context.Context.Request.Query.ContainsKey("v")
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    }
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorPages();
app.MapHealthChecks("/healthz");

// Settings → Export: the signed-in user's own data. Plain GET downloads are fine here: another
// site can make the browser fetch them but cannot read the response.
var export = app.MapGroup("/export").RequireAuthorization();
export.MapGet("/json", (HttpContext context, IExportService exporter) =>
    ExportAsync(context, exporter, "json", "application/json", exporter.ToJson));
export.MapGet("/xlsx", (HttpContext context, IExportService exporter) =>
    ExportAsync(context, exporter, "xlsx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", exporter.ToExcel));

app.MapRazorComponents<PomodoroRelax4.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program
{
    private static async Task<IResult> ExportAsync(HttpContext context, IExportService exporter,
        string extension, string contentType, Func<UserDataExport, byte[]> write)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var data = userId is null ? null : await exporter.GetUserDataAsync(userId);
        if (data is null)
            return Results.Unauthorized();

        var fileName = $"pomodororelax-{data.Account.UserName}-{DateTime.UtcNow:yyyyMMdd}.{extension}";
        return Results.File(write(data), contentType, fileName);
    }

    // Scripts only from this app (no inline script, no CDN). Styles allow 'unsafe-inline' because
    // the Tailwind Play script injects a <style> element and components use style attributes.
    // connect-src 'self' covers Blazor's WebSocket to the same host.
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data:; " +
        "media-src 'self'; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";
}
