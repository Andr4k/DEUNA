using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Deuna.Delivery.Service.Models;

public class DeliveryDbContext : DbContext
{
    public DeliveryDbContext(DbContextOptions<DeliveryDbContext> options) : base(options) { }

    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryTracking> DeliveryTrackings => Set<DeliveryTracking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.Entity<Delivery>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrderId).IsRequired();
            entity.Property(e => e.CourierId).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.OwnsOne(e => e.PickupAddress);
            entity.OwnsOne(e => e.DropoffAddress);
            entity.HasIndex(e => e.OrderId).IsUnique();
            entity.HasIndex(e => e.CourierId);
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<DeliveryTracking>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DeliveryId).IsRequired();
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.Location).HasColumnType("geography (point)");
            entity.HasOne(e => e.Delivery)
                .WithMany(d => d.Trackings)
                .HasForeignKey(e => e.DeliveryId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public class Delivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid CourierId { get; set; }
    public string Status { get; set; } = "Assigned";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PickedUpAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public Address PickupAddress { get; set; } = new();
    public Address DropoffAddress { get; set; } = new();
    public List<DeliveryTracking> Trackings { get; set; } = new();
}

public class DeliveryTracking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeliveryId { get; set; }
    public Point Location { get; set; } = null!;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? StatusNote { get; set; }
    public Delivery? Delivery { get; set; }
}

public class Address
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public Point? Location { get; set; }
}
