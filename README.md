# PomodoroRelax4

Pomodoro timer with tasks, per-user settings and statistics. Blazor Server on .NET 10, ASP.NET Core Identity, EF Core on PostgreSQL or SQL Server.

## Run locally

```powershell
cd PomodoroRelax4
dotnet run
```

Set `ConnectionStrings:DefaultConnection` (in `appsettings.Development.json` or user secrets) and pick the database with `Database:Provider`:

| `Database:Provider` | Connection string example |
| --- | --- |
| `SqlServer` (default) | `Server=localhost;Database=PomodoroRelax4;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=true` |
| `PostgreSql` | `Host=localhost;Port=5432;Database=pomodoro;Username=postgres;Password=...` |

The app creates the database and applies pending migrations on startup.

### Migrations

EF Core migrations are provider-specific, so each provider has its own set: `ApplicationDbContext` owns the SQL Server ones in `Migrations/`, and `PostgresDbContext` (a subclass with the same model) owns the PostgreSQL ones in `Migrations/PostgreSql/`. After a model change, add the migration to both:

```powershell
cd PomodoroRelax4
dotnet ef migrations add <Name> --context ApplicationDbContext
dotnet ef migrations add <Name> --context PostgresDbContext --output-dir Migrations/PostgreSql --namespace PomodoroRelax4.Migrations.PostgreSql
```

Store dates as UTC (`DateTime.UtcNow`): PostgreSQL maps `DateTime` to `timestamp with time zone`, which rejects local times.

## Accounts

Public sign-up is closed. `Admin:Email` (env `Admin__Email`) names the administrator:

1. Set it and start the app. `/Account/Register` now accepts that e-mail only, and only until it has an account.
2. Register the admin account right away; after that the register page just says registration is closed.
3. Signed in as admin, open **Users** in the top bar (`/Admin/Users`) to create accounts for everyone else and hand them their password.

Users change their own password from **Settings → Change password** (`/Account/ChangePassword`). Under **Manage** on each user, the admin can set a new password (which also clears a lockout from failed sign-ins) or delete the user together with their tasks, sessions and settings. The admin account itself is managed through `Admin:Email`, not from this page. A reset or deleted user's other browsers are signed out at the next security-stamp check, within 30 minutes.

The `Admin` role always follows the configured e-mail: on startup it is given to that account and taken from any other, so changing `Admin:Email` and restarting moves it (the old admin sees the change at their next sign-in).

## Deploy (Podman)

`Containerfile` builds one image for the app. `deploy/quadlet` runs it as systemd services next to PostgreSQL 18: a shared `pomodoro` network, a `pomodoro-db` volume for the database and a `pomodoro-keys` volume for ASP.NET Core Data Protection keys. Keep that second volume: losing it signs everyone out. The app applies migrations itself on startup; the database unit reports ready only once it accepts connections, and the app unit restarts if a migration still fails. Quadlet needs Podman 5.0 or newer.

To run on SQL Server 2022 (Express edition) instead, copy `deploy/quadlet/sqlserver/*` over the PostgreSQL `pomodoro-db.container` and use the SQL Server block of the env file. Its data goes to a `pomodoro-mssql` volume, and the image needs an x86-64 host with about 2 GB of RAM. Switching provider starts from an empty database; nothing is copied across.

Every push to `main` runs `.github/workflows/container.yml`, which builds the `Containerfile` and pushes `ghcr.io/anilkay/pomodororelax4` tagged `latest` and with the full commit SHA. The package is public, so the server pulls it without logging in.

```bash
# Rootless (the user needs lingering so services survive logout):
mkdir -p ~/.config/pomodoro ~/.config/containers/systemd
cp deploy/pomodoro.env.example ~/.config/pomodoro/pomodoro.env   # set the DB password twice and Admin__Email
chmod 600 ~/.config/pomodoro/pomodoro.env
cp deploy/quadlet/pomodoro* ~/.config/containers/systemd/   # skip pomodoro-tunnel.container until tunnel.env exists
# SQL Server instead: cp deploy/quadlet/sqlserver/* ~/.config/containers/systemd/
loginctl enable-linger "$USER"
systemctl --user daemon-reload
systemctl --user start pomodoro-app
journalctl --user -u pomodoro-app -f
```

Then register the admin e-mail on `/Account/Register` before anyone else can (see [Accounts](#accounts)).

For a rootful install use `/etc/pomodoro/pomodoro.env`, `/etc/containers/systemd/` and `systemctl` without `--user`; the units find the env file through `%E` in both cases.

The app unit carries `AutoUpdate=registry`. `podman auto-update` (or enabling `podman-auto-update.timer`) pulls a newer `latest` and restarts the app, which applies any new migrations as it starts. To pin a release, replace `latest` with a `sha-<commit>` tag. To run a locally built image instead, `podman build -t localhost/pomodororelax4:latest -f Containerfile .` and point `Image=` at it.

The app listens on `127.0.0.1:8082` over plain HTTP. Put a TLS-terminating proxy in front of it (Cloudflare Tunnel below, or Caddy/nginx); `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` in the env file makes the app trust its `X-Forwarded-*` headers, so the sign-in cookie is marked `Secure` and the registration rate limit (5 per hour per IP) sees the real client address.

`GET /healthz` returns `Healthy` (200) when the app can reach its database and `Unhealthy` (503) otherwise. Point an uptime monitor at it.

### Cloudflare Tunnel

`pomodoro-tunnel` runs `cloudflared` on the `pomodoro` network and opens an outbound-only connection to Cloudflare, which serves the site over HTTPS on your own domain. No inbound port is needed on the server.

1. In Cloudflare **Zero Trust > Networks > Tunnels**, create a tunnel (type *Cloudflared*) and copy the token from the install command.
2. Under the tunnel's **Public Hostname** tab, add your hostname (for example `pomodoro.example.com`) with service type `HTTP` and URL `pomodoro-app:8080`. The domain's DNS must be on Cloudflare.
3. On the server:

```bash
cp deploy/tunnel.env.example ~/.config/pomodoro/tunnel.env   # set TUNNEL_TOKEN, no quotes
chmod 600 ~/.config/pomodoro/tunnel.env
cp deploy/quadlet/pomodoro-tunnel.container ~/.config/containers/systemd/
systemctl --user daemon-reload
systemctl --user start pomodoro-tunnel
journalctl --user -u pomodoro-tunnel -f   # wait for "Registered tunnel connection"
```

Blazor Server keeps a WebSocket open per browser tab; Cloudflare Tunnel passes WebSockets through without extra settings. If you use nginx instead, forward the `Upgrade` and `Connection` headers.

### Compose

`deploy/compose.yaml` describes the same stack for `podman compose -f deploy/compose.yaml up -d --build` (with the env file at `deploy/pomodoro.env`), which is handy for trying the image locally; `deploy/compose.sqlserver.yaml` is the SQL Server variant. Add `--profile tunnel` and `deploy/tunnel.env` to start the tunnel too.

### Backups

PostgreSQL (`pomodoro-db` volume):

```bash
podman exec pomodoro-db pg_dump -U pomodoro -Fc pomodoro > pomodoro.dump
# restore: podman exec -i pomodoro-db pg_restore -U pomodoro -d pomodoro --clean < pomodoro.dump
```

SQL Server (`pomodoro-mssql` volume):

```bash
podman exec pomodoro-db /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P '<password>' \
  -Q "BACKUP DATABASE PomodoroRelax4 TO DISK='/var/opt/mssql/data/pomodoro.bak' WITH INIT"
podman cp pomodoro-db:/var/opt/mssql/data/pomodoro.bak .
```
