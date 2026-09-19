using Microsoft.EntityFrameworkCore;

namespace Deuna.Feedback.Service.Models;

public class FeedbackDbContext : DbContext
{
    public FeedbackDbContext(DbContextOptions<FeedbackDbContext> options) : base(options) { }

    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<FeedbackRating> FeedbackRatings => Set<FeedbackRating>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Feedback>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrderId).IsRequired();
            entity.Property(e => e.CustomerId).IsRequired();
            entity.Property(e => e.Comment).HasMaxLength(2000);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.HasIndex(e => e.OrderId).IsUnique();
        });

        modelBuilder.Entity<FeedbackRating>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FeedbackId).IsRequired();
            entity.Property(e => e.Criteria).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Score).IsRequired();
            entity.HasOne(e => e.Feedback)
                .WithMany(f => f.Ratings)
                .HasForeignKey(e => e.FeedbackId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public class Feedback
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<FeedbackRating> Ratings { get; set; } = new();
}

public class FeedbackRating
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FeedbackId { get; set; }
    public string Criteria { get; set; } = string.Empty;
    public int Score { get; set; }
    public Feedback? Feedback { get; set; }
}
