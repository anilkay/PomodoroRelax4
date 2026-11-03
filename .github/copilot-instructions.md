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

Each context has its own design-time factory (`ApplicationDbContextDesignTimeFactory`, `PostgresDbContextDesignTimeFactory`). Without the SQL Server one, the tools would build `ApplicationDbContext` from the PostgreSQL factory and put a PostgreSQL migration into `Migrations/PostgreSql`. A new non-nullable column gets the same default in both migrations; check the generated `defaultValue`.

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

### Loading user data in Blazor components
Interactive pages are prerendered: the server renders them once as plain HTML, then the circuit renders them again. Load the data in `OnInitializedAsync` and keep it in a `[PersistentState]` public property. The prerender fills it, it travels inside the page, and the circuit starts from it. The first HTML then already shows the data, and the database is queried once instead of twice:

```csharp
@inject AuthenticationStateProvider AuthStateProvider

[PersistentState]
public List<PomodoroTask>? PrerenderedTasks { get; set; }

protected override async Task OnInitializedAsync()
{
    var state = await AuthStateProvider.GetAuthenticationStateAsync();
    userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (userId is not null)
        PrerenderedTasks ??= await TaskService.GetTasksAsync(userId);
}
```

Put `[PersistentState]` on a component the page renders, never on the routable page with `@rendermode` itself. That page is the root of the circuit, and on it ASP.NET Core 10.0.13 throws a `NullReferenceException` in `RemoteRenderer.GetMarkerKey` while restoring the value, which kills the circuit, so the page never goes interactive. `Stats.razor` and `Settings.razor` are therefore thin pages around `StatsPanel` and `SettingsForm`; `Home` has the state in `TaskList` and `TimerDisplay`.

JS interop doesn't work while prerendering. Browser values the server needs then (the viewer's UTC offset, "hide done tasks") are cookies that `site.js` writes, read through `Services/ClientPreferences.cs` from `[CascadingParameter] HttpContext? HttpContext`; that is null once the circuit runs. When a cookie is missing (first visit), fall back to JS in `OnAfterRenderAsync`, as `Stats.razor` does. Don't use `[CascadingParameter] Task<AuthenticationState>`; inject `AuthenticationStateProvider` as above.

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
- **Tailwind 3.4.17, compiled at build time.** Config is `tailwind.config.js` and input is `Styles/tailwind.css`. The output `wwwroot/css/tailwind.css` holds only the classes found in `.razor` / `.cshtml` / `wwwroot/js`, so write class names out in full, never assembled from pieces at runtime.
  - The `TailwindCss` target in the `.csproj` regenerates it on build when the CLI is in `.tools/` (`sh tools/get-tailwind.sh`). Commit the regenerated file with markup changes.
  - The Containerfile always regenerates it.
- **Fonts** are self-hosted in `wwwroot/fonts`. The `@font-face` rules are in `app.css`, and the CSP allows no font or style origin but `'self'`.
- **Light and dark themes.** `js/theme.js` sets `data-theme` on `<html>` before the first paint, from localStorage or else the system setting. The ☀/☾ button (`data-theme-toggle`, handled in `site.js`) switches it. Theme colours are CSS variables in `wwwroot/css/app.css`. Never hard-code `white`, `rgba(255,255,255,…)` or `#0c0c18`:
  - `text-fg`, `bg-fg/10`, `border-fg/20` and so on for the foreground: white on dark, ink on light.
  - `bg-surface` for cards and `bg-field` for text inputs.
  - `text-danger` for validation errors.
  - `rgb(var(--fg) / 0.4)` and `rgb(var(--bg))` in inline styles. SVG presentation attributes don't resolve `var()`, so use `style="stroke: …"`.
  - `text-app-base` (fixed dark ink) for text on accent-coloured fills.
- **Glass-morphism cards**: use the `.glass` CSS class, theme-aware.
- **Static file URLs carry a content hash.** That is `asp-append-version="true"` in `_AuthLayout.cshtml` and `@V("/path")` in `App.razor`. Hashed URLs are cached for a year and others revalidate (see `UseStaticFiles` in `Program.cs`), so add new `wwwroot` CSS/JS references the same way or browsers may keep an old copy after a deploy.
- **Timer digits**: use class `timer-digits` (JetBrains Mono, tabular-nums).
- All color accents are computed from `TimerMode` with a C# switch expression. Never hardcode mode colors — add a new arm to the existing switches in `TimerDisplay.razor` and `MainLayout.razor`.

### EF Core queries with optional joins
Do **not** use `.Join().DefaultIfEmpty()` for left joins in EF Core — it does not translate correctly. Use the two-step pattern instead (fetch parent list → fetch related items as `Dictionary` → join in memory). See `StatsService.GetStatsAsync` for the canonical example.

### Services
Each domain has an interface + implementation in `Services/`:
- `ITaskService` / `TaskService` — CRUD, reorder and clear-done for `PomodoroTask`. Priority, due date (`DateOnly`, the user's own day) and notes are carried through `ExportService` / `ImportService` as well.
- `ISessionService` / `SessionService` — record completed timer sessions
- `ISettingsService` / `SettingsService` — per-user timer durations
- `IStatsService` / `StatsService` — the Stats page's view model: counts, streaks, the 7-day / 30-day / 12-month activity with hours and top tasks, and a year of daily counts for the heatmap. It reads one window of pomodoros (the heatmap's year, or two years for the 12-month view) and groups them in memory by the viewer's local day.

All registered as `Scoped` in `Program.cs`.

### Adding a new page
1. Create `Components/Pages/MyPage.razor` with `@page "/route"`, `@attribute [Authorize]`, `@rendermode InteractiveServer`, and put `<SignOutGuard />` in it. Without the guard, an open tab of a deleted user, or of one whose password changed, keeps working.
   - No inline `<script>` or `on…=""` handlers: the Content-Security-Policy in `Program.cs` allows scripts only from `wwwroot`.
2. Put the content in a child component (`MyPagePanel.razor`) that the page renders. There, use `@inject AuthenticationStateProvider AuthStateProvider` and load user data in `OnInitializedAsync` into a `[PersistentState]` property (see "Loading user data in Blazor components").
3. Add a nav link in `Components/Layout/MainLayout.razor`.

### IIS deployment
- Site: `PomodoroRelax4`, port `8080`, physical path `C:\inetpub\PomodoroRelax4`
- App pool: `PomodoroRelax4`, No Managed Code, Integrated Pipeline
- DB auth: Windows Authentication — `IIS APPPOOL\PomodoroRelax4` has `db_datareader`, `db_datawriter`, `db_ddladmin`, `EXECUTE` on the `PomodoroRelax4` SQL Server database
- Connection string uses `Trusted_Connection=True;TrustServerCertificate=True`
