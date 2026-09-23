using Microsoft.EntityFrameworkCore;

namespace Deuna.Notification.Service.Models;

public class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options) : base(options) { }

    public DbSet<Dispositivo> Dispositivos => Set<Dispositivo>();
    public DbSet<NotificacionEnviada> NotificacionesEnviadas => Set<NotificacionEnviada>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Dispositivo>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UsuarioId).IsRequired();
            entity.Property(e => e.Token).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Plataforma).IsRequired().HasMaxLength(20);
            entity.Property(e => e.CreatedAt).IsRequired();

            // Un token pertenece a un dispositivo: si el mismo teléfono cambia de usuario,
            // se reasigna en lugar de duplicarse.
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => new { e.UsuarioId, e.Activo });
        });

        modelBuilder.Entity<NotificacionEnviada>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TipoEvento).IsRequired().HasMaxLength(50);
            entity.Property(e => e.DestinatarioId).IsRequired();
            entity.Property(e => e.Titulo).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Cuerpo).HasMaxLength(500);
            entity.Property(e => e.Estado).IsRequired().HasMaxLength(30);
            entity.Property(e => e.Proveedor).HasMaxLength(50);
            entity.Property(e => e.Error).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).IsRequired();

            // Idempotencia: un mensaje del broker se notifica una sola vez.
            // En PostgreSQL los NULL no colisionan en un índice único, así que los envíos
            // sin MensajeId (por ejemplo, manuales) no se bloquean entre sí.
            entity.HasIndex(e => e.MensajeId).IsUnique();
            entity.HasIndex(e => e.DestinatarioId);
            entity.HasIndex(e => e.TipoEvento);
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}
