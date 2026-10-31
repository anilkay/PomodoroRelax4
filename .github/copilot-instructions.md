# Copilot Instructions — PomodoroRelax4

## Build & Run

```powershell
# Run (development)
cd PomodoroRelax4
dotnet run

# Build only
dotnet build

# Publish to IIS deploy folder
dotnet publish -c Release -o C:\inetpub\PomodoroRelax4

# Recycle IIS after publish
C:\Windows\System32\inetsrv\appcmd.exe recycle apppool /apppool.name:"PomodoroRelax4"
```

No test project exists in this solution.

### EF Core migrations

`Database:Provider` selects `SqlServer` (default) or `PostgreSql`. Migrations are provider-specific, so every model change needs one migration per provider:

```powershell
cd PomodoroRelax4
dotnet ef migrations add <MigrationName> --context ApplicationDbContext
dotnet ef migrations add <MigrationName> --context PostgresDbContext --output-dir Migrations/PostgreSql --namespace PomodoroRelax4.Migrations.PostgreSql
```

`PostgresDbContext` (in `Data/PostgresDbContext.cs`) subclasses `ApplicationDbContext` only to own the PostgreSQL migrations; services keep depending on `IDbContextFactory<ApplicationDbContext>`. Always write `DateTime.UtcNow` — PostgreSQL's `timestamp with time zone` rejects local times.

Migrations are applied automatically on startup (`db.Database.Migrate()` in `Program.cs`).

---

## Architecture

**Blazor Server** (.NET 10) with two distinct rendering layers:

| Layer | Path | Purpose |
|-------|------|---------|
| Blazor components | `Components/Pages/` | All app UI — timer, tasks, stats, settings |
| Razor Pages | `Pages/Account/`, `Pages/Admin/` | Auth and account management (Login, Register, Logout, ChangePassword, Admin/Users) |

### Why two layers?
Cookie auth (`SignInManager`) **cannot** be called from within a Blazor Server circuit — the response has already started. All sign-in/sign-out operations must go through Razor Pages, which run in the normal HTTP pipeline.

### Auth state in Blazor components
`[CascadingParameter] Task<AuthenticationState>` is unreliable during Blazor Server circuit startup. The pattern used throughout this app:

```csharp
// ✅ Correct pattern for all components and pages
@inject AuthenticationStateProvider AuthStateProvider

protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (!firstRender) return;
    var state = await AuthStateProvider.GetAuthenticationStateAsync();
    userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
    // load data...
    StateHasChanged();
}
```

### DbContext
Uses `IDbContextFactory<ApplicationDbContext>` everywhere — **never inject `ApplicationDbContext` directly** into Blazor services. Each service method creates and disposes its own context:

```csharp
await using var db = await _factory.CreateDbContextAsync();
```

### State flow on Home page
`Home.razor` owns the active task selection and timer mode. It passes them down to `TimerDisplay` and `TaskList` via `[Parameter]` and receives updates via `EventCallback`. The current `TimerMode` is passed up to `MainLayout` as a `[CascadingValue]` to drive the background gradient.

---

## Key Conventions

### Styling
- **Tailwind Play 3.4.17**, served from `wwwroot/lib/tailwindcss/` and configured in `wwwroot/js/tailwind.config.js`; both layouts load it.
- **Light and dark themes.** `js/theme.js` sets `data-theme` on `<html>` before the first paint, from localStorage or else the system setting. The ☀/☾ button (`data-theme-toggle`, handled in `site.js`) switches it. Theme colours are CSS variables in `wwwroot/css/app.css`. Never hard-code `white`, `rgba(255,255,255,…)` or `#0c0c18`:
  - `text-fg`, `bg-fg/10`, `border-fg/20` and so on for the foreground: white on dark, ink on light.
  - `bg-surface` for cards and `bg-field` for text inputs.
  - `text-danger` for validation errors.
  - `rgb(var(--fg) / 0.4)` and `rgb(var(--bg))` in inline styles. SVG presentation attributes don't resolve `var()`, so use `style="stroke: …"`.
  - `text-app-base` (fixed dark ink) for text on accent-coloured fills.
- **Glass-morphism cards**: use the `.glass` CSS class, theme-aware.
- **Timer digits**: use class `timer-digits` (JetBrains Mono, tabular-nums).
- All color accents are computed from `TimerMode` with a C# switch expression. Never hardcode mode colors — add a new arm to the existing switches in `TimerDisplay.razor` and `MainLayout.razor`.

### EF Core queries with optional joins
Do **not** use `.Join().DefaultIfEmpty()` for left joins in EF Core — it does not translate correctly. Use the two-step pattern instead (fetch parent list → fetch related items as `Dictionary` → join in memory). See `StatsService.GetStatsAsync` for the canonical example.

### Services
Each domain has an interface + implementation in `Services/`:
- `ITaskService` / `TaskService` — CRUD + reorder for `PomodoroTask`
- `ISessionService` / `SessionService` — record completed timer sessions
- `ISettingsService` / `SettingsService` — per-user timer durations
- `IStatsService` / `StatsService` — aggregate statistics view model

All registered as `Scoped` in `Program.cs`.

### Adding a new page
1. Create `Components/Pages/MyPage.razor` with `@page "/route"`, `@attribute [Authorize]`, `@rendermode InteractiveServer`, and put `<SignOutGuard />` in it. Without the guard, an open tab of a deleted user, or of one whose password changed, keeps working.
   - No inline `<script>` or `on…=""` handlers: the Content-Security-Policy in `Program.cs` allows scripts only from `wwwroot`.
2. Use `@inject AuthenticationStateProvider AuthStateProvider` and the `OnAfterRenderAsync(firstRender)` pattern to load user data.
3. Add a nav link in `Components/Layout/MainLayout.razor`.

### IIS deployment
- Site: `PomodoroRelax4`, port `8080`, physical path `C:\inetpub\PomodoroRelax4`
- App pool: `PomodoroRelax4`, No Managed Code, Integrated Pipeline
- DB auth: Windows Authentication — `IIS APPPOOL\PomodoroRelax4` has `db_datareader`, `db_datawriter`, `db_ddladmin`, `EXECUTE` on the `PomodoroRelax4` SQL Server database
- Connection string uses `Trusted_Connection=True;TrustServerCertificate=True`
