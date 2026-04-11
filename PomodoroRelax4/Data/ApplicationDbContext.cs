using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PomodoroRelax4.Models;

namespace PomodoroRelax4.Data;

public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
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
