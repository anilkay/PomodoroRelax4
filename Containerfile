# PomodoroRelax4 image. Migrations are applied by the app itself on startup
# (db.Database.Migrate() in Program.cs), so no separate migrate step is needed.
#   podman build -t localhost/pomodororelax4:latest -f Containerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PomodoroRelax4/PomodoroRelax4.csproj PomodoroRelax4/
RUN dotnet restore PomodoroRelax4/PomodoroRelax4.csproj

# Tailwind comes from the CDN in App.razor, so no Node step is needed here.
COPY PomodoroRelax4/ PomodoroRelax4/
RUN dotnet publish PomodoroRelax4/PomodoroRelax4.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Data Protection keys (the sign-in cookie, antiforgery tokens) live here.
# Pre-create it owned by the app user so a fresh named volume inherits that
# ownership on first mount; without the volume every restart signs users out.
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys \
 && chown -R app:app /home/app/.aspnet

COPY --from=build /app ./

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER app

CMD ["dotnet", "PomodoroRelax4.dll"]
