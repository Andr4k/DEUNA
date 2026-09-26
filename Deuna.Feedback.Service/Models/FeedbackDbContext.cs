using Microsoft.EntityFrameworkCore;

namespace Deuna.Feedback.Service.Models;

public class FeedbackDbContext : DbContext
{
    public FeedbackDbContext(DbContextOptions<FeedbackDbContext> options) : base(options) { }

    /// <summary>Encuestas de feedback Nivel 1.</summary>
    public DbSet<FeedbackEncuesta> FeedbackEncuestas => Set<FeedbackEncuesta>();

    /// <summary>Read-model de pedidos replicados desde Orders por eventos.</summary>
    public DbSet<PedidoReplicado> PedidosReplicados => Set<PedidoReplicado>();

    /// <summary>Read-model de repartidores replicados desde Identity por eventos.</summary>
    public DbSet<RepartidorReplicado> RepartidoresReplicados => Set<RepartidorReplicado>();

    /// <summary>Read-model de restaurantes replicados desde Identity por eventos.</summary>
    public DbSet<RestauranteReplicado> RestaurantesReplicados => Set<RestauranteReplicado>();

    /// <summary>Criterios del feedback Nivel 2, una fila por criterio (FR-004.3).</summary>
    public DbSet<FeedbackDetalleCriterio> FeedbackDetallesCriterios => Set<FeedbackDetalleCriterio>();

    /// <summary>Comentario y foto del Nivel 2 (opcional, 1:1 con la encuesta).</summary>
    public DbSet<FeedbackComentarioFoto> FeedbackComentariosFotos => Set<FeedbackComentarioFoto>();

    /// <summary>Compartidos por WhatsApp (US-004.3).</summary>
    public DbSet<FeedbackViralWhatsApp> CompartidosWhatsApp => Set<FeedbackViralWhatsApp>();

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

        modelBuilder.Entity<RepartidorReplicado>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.NombreCompleto).IsRequired().HasMaxLength(200);
            entity.Property(e => e.DocumentoIdentidad).HasMaxLength(50);
            entity.Property(e => e.CiudadOperacion).HasMaxLength(100);
            entity.Property(e => e.FotoPerfilUrl).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => e.Activo);
        });

        modelBuilder.Entity<RestauranteReplicado>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.NombreComercial).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Ciudad).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => e.Activo);
        });

        modelBuilder.Entity<FeedbackDetalleCriterio>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Criterio).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Puntaje).IsRequired();

            // Un criterio se evalúa una sola vez por encuesta: dos puntajes para el mismo
            // criterio serían un dato ambiguo, y el índice también corta la carrera de dos
            // envíos simultáneos.
            entity.HasIndex(e => new { e.EncuestaId, e.Criterio }).IsUnique();

            entity.ToTable("feedback_detalle_criterios", t =>
            {
                t.HasCheckConstraint("CK_feedback_detalle_criterios_puntaje", "\"Puntaje\" BETWEEN 1 AND 5");
            });
        });

        modelBuilder.Entity<FeedbackComentarioFoto>(entity =>
        {
            entity.HasKey(e => e.EncuestaId);
            entity.Property(e => e.ComentarioTexto).HasMaxLength(1000);
            entity.Property(e => e.UrlFotoEvidencia).HasMaxLength(500);
        });

        modelBuilder.Entity<FeedbackViralWhatsApp>(entity =>
        {
            entity.HasKey(e => e.EncuestaId);
            entity.Property(e => e.CodigoCompartido).IsRequired().HasMaxLength(8);

            // El enlace compartido se reenvía fuera de la app: el código tiene que ser único.
            entity.HasIndex(e => e.CodigoCompartido).IsUnique();
            entity.HasIndex(e => e.FueCompartido);
        });
    }
}
