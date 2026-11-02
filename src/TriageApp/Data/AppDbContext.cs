using Microsoft.EntityFrameworkCore;

namespace TriageApp.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<QuizProgress> QuizProgress => Set<QuizProgress>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ticket>()
            .Property(t => t.Status)
            .HasConversion<int>();

        modelBuilder.Entity<QuizProgress>()
            .HasIndex(p => new { p.UserId, p.QuestionId })
            .IsUnique();

        modelBuilder.Entity<QuizProgress>()
            .Property(p => p.UpdatedAtUtc)
            .HasDefaultValueSql("GETUTCDATE()");
    }
}
