using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PomodoroRelax4.Models;

namespace PomodoroRelax4.Data;

public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    // For provider-specific subclasses (PostgresDbContext) that carry their own migrations
    protected ApplicationDbContext(DbContextOptions options)
        : base(options) { }

    public DbSet<PomodoroTask> PomodoroTasks => Set<PomodoroTask>();
    public DbSet<PomodoroSession> PomodoroSessions => Set<PomodoroSession>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<PomodoroTask>(e =>
        {
            e.HasIndex(t => new { t.UserId, t.SortOrder });
        });

        builder.Entity<PomodoroSession>(e =>
        {
            e.HasOne(s => s.Task)
             .WithMany()
             .HasForeignKey(s => s.TaskId)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(s => s.UserId);
        });
    }
}

/// <summary>
/// Lets <c>dotnet ef migrations add ... --context ApplicationDbContext</c> build the SQL Server
/// context. Without it the tools pick PostgresDbContextDesignTimeFactory (PostgresDbContext is an
/// ApplicationDbContext) and write a PostgreSQL migration. The connection string is never opened.
/// </summary>
public class ApplicationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=PomodoroRelax4")
            .Options);
}
