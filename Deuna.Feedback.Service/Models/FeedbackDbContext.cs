using Microsoft.EntityFrameworkCore;

namespace Deuna.Feedback.Service.Models;

public class FeedbackDbContext : DbContext
{
    public FeedbackDbContext(DbContextOptions<FeedbackDbContext> options) : base(options) { }

    /// <summary>Encuestas de feedback Nivel 1.</summary>
    public DbSet<FeedbackEncuesta> FeedbackEncuestas => Set<FeedbackEncuesta>();

    /// <summary>Read-model de pedidos replicados desde Orders por eventos.</summary>
    public DbSet<PedidoReplicado> PedidosReplicados => Set<PedidoReplicado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FeedbackEncuesta>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PedidoId).IsRequired();
            entity.Property(e => e.RestauranteId).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            // Un pedido solo puede recibir un feedback Nivel 1
            entity.HasIndex(e => e.PedidoId).IsUnique();
            entity.HasIndex(e => e.RestauranteId);
            entity.HasIndex(e => e.RepartidorId);

            // Restricciones del plan técnico: ratings entre 1 y 5
            entity.ToTable("feedback_encuestas", t =>
            {
                t.HasCheckConstraint("CK_feedback_encuestas_rating_comida", "\"RatingGeneralComida\" BETWEEN 1 AND 5");
                t.HasCheckConstraint("CK_feedback_encuestas_rating_repartidor", "\"RatingServicioRepartidor\" BETWEEN 1 AND 5");
            });
        });

        modelBuilder.Entity<PedidoReplicado>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PedidoId).IsRequired();
            entity.Property(e => e.Codigo).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Estado).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Total).HasPrecision(18, 2);

            // Idempotencia de la proyección: un pedido, una fila
            entity.HasIndex(e => e.PedidoId).IsUnique();
            entity.HasIndex(e => e.Estado);
            entity.HasIndex(e => e.RestauranteId);
        });
    }
}
