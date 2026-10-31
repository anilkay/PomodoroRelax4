using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PomodoroRelax4.Data;

/// <summary>
/// PostgreSQL flavour of <see cref="ApplicationDbContext"/>: same model, but EF migrations
/// are provider-specific, so this type owns the ones in Migrations/PostgreSql while
/// ApplicationDbContext keeps the SQL Server ones in Migrations/.
/// </summary>
public class PostgresDbContext : ApplicationDbContext
{
    public PostgresDbContext(DbContextOptions<PostgresDbContext> options)
        : base(options) { }
}

/// <summary>
/// Lets <c>dotnet ef migrations add ... --context PostgresDbContext</c> build the context
/// without a database or the app's config; the connection string is never opened.
/// </summary>
public class PostgresDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PostgresDbContext>
{
    public PostgresDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<PostgresDbContext>()
            .UseNpgsql("Host=localhost;Database=pomodoro")
            .Options);
}

/// <summary>
/// Exposes a subclass factory as <c>IDbContextFactory&lt;ApplicationDbContext&gt;</c>, which is
/// what the services and Identity ask for.
/// </summary>
public sealed class DerivedDbContextFactory<TContext> : IDbContextFactory<ApplicationDbContext>
    where TContext : ApplicationDbContext
{
    private readonly IDbContextFactory<TContext> _inner;

    public DerivedDbContextFactory(IDbContextFactory<TContext> inner) => _inner = inner;

    public ApplicationDbContext CreateDbContext() => _inner.CreateDbContext();

    public async Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => await _inner.CreateDbContextAsync(cancellationToken);
}
