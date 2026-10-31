# PomodoroRelax4

Pomodoro timer with tasks, per-user settings and statistics. Blazor Server on .NET 10, ASP.NET Core Identity, EF Core on SQL Server.

## Run locally

```powershell
cd PomodoroRelax4
dotnet run
```

Set `ConnectionStrings:DefaultConnection` (in `appsettings.Development.json` or user secrets) to a SQL Server database. The app applies pending migrations on startup.

## Deploy (Podman)

`Containerfile` builds one image for the app. `deploy/quadlet` runs it as systemd services next to SQL Server 2022 (Express edition): a shared `pomodoro` network, a `pomodoro-db` volume for the database and a `pomodoro-keys` volume for ASP.NET Core Data Protection keys. Keep that second volume: losing it signs everyone out. The app creates the database and applies migrations itself on startup; the database unit reports ready only once SQL Server accepts connections, and the app unit restarts if a migration still fails. Quadlet needs Podman 5.0 or newer, and the SQL Server image needs an x86-64 host with about 2 GB of RAM.

Every push to `main` runs `.github/workflows/container.yml`, which builds the `Containerfile` and pushes `ghcr.io/anilkay/pomodororelax4` tagged `latest` and with the full commit SHA. The repository is public, so the package is public too and the server can pull it without logging in (if it stays private, `podman login ghcr.io` with a token that has `read:packages`, using `--authfile ~/.config/containers/auth.json` so it survives a reboot).

```bash
# Rootless (the user needs lingering so services survive logout):
mkdir -p ~/.config/pomodoro ~/.config/containers/systemd
cp deploy/pomodoro.env.example ~/.config/pomodoro/pomodoro.env   # set the SA password twice
chmod 600 ~/.config/pomodoro/pomodoro.env
cp deploy/quadlet/* ~/.config/containers/systemd/   # skip pomodoro-tunnel.container until tunnel.env exists
loginctl enable-linger "$USER"
systemctl --user daemon-reload
systemctl --user start pomodoro-app
journalctl --user -u pomodoro-app -f
```

For a rootful install use `/etc/pomodoro/pomodoro.env`, `/etc/containers/systemd/` and `systemctl` without `--user`; the units find the env file through `%E` in both cases.

The app unit carries `AutoUpdate=registry`. `podman auto-update` (or enabling `podman-auto-update.timer`) pulls a newer `latest` and restarts the app, which applies any new migrations as it starts. To pin a release, replace `latest` with a `sha-<commit>` tag. To run a locally built image instead, `podman build -t localhost/pomodororelax4:latest -f Containerfile .` and point `Image=` at it.

The app listens on `127.0.0.1:8082` over plain HTTP. Put a TLS-terminating proxy in front of it (Cloudflare Tunnel below, or Caddy/nginx); `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` in the env file makes the app trust its `X-Forwarded-*` headers, so the sign-in cookie is marked `Secure` and the registration rate limit (5 per hour per IP) sees the real client address.

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

`deploy/compose.yaml` describes the same stack for `podman compose -f deploy/compose.yaml up -d --build` (with the env file at `deploy/pomodoro.env`), which is handy for trying the image locally. Add `--profile tunnel` and `deploy/tunnel.env` to start the tunnel too.

### Backups

The database lives in the `pomodoro-db` volume. A consistent copy:

```bash
podman exec pomodoro-db /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P '<password>' \
  -Q "BACKUP DATABASE PomodoroRelax4 TO DISK='/var/opt/mssql/data/pomodoro.bak' WITH INIT"
podman cp pomodoro-db:/var/opt/mssql/data/pomodoro.bak .
```
