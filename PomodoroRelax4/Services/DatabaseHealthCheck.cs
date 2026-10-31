using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PomodoroRelax4.Data;

namespace PomodoroRelax4.Services;

/// <summary>Healthy when the configured database answers; backs <c>/healthz</c>.</summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public DatabaseHealthCheck(IDbContextFactory<ApplicationDbContext> factory) => _factory = factory;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to the database.");
    }
}
