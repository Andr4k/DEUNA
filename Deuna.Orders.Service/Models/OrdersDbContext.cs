using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Deuna.Orders.Service.Models;

namespace Deuna.Orders.Service.Models;

public class OrdersDbContext : DbContext
{
    public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options) { }

    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<ItemPedido> ItemsPedido => Set<ItemPedido>();
    public DbSet<DireccionEntrega> DireccionesEntrega => Set<DireccionEntrega>();
    public DbSet<ContactoPedido> ContactosPedido => Set<ContactoPedido>();
    public DbSet<TarifaAplicada> TarifasAplicadas => Set<TarifaAplicada>();
    public DbSet<RestauranteReplicado> RestaurantesReplicados => Set<RestauranteReplicado>();
    public DbSet<ZonaCoberturaReplicada> ZonasCoberturaReplicadas => Set<ZonaCoberturaReplicada>();
    public DbSet<HorarioAtencionReplicado> HorariosAtencionReplicados => Set<HorarioAtencionReplicado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("postgis");

        // Pedido
        modelBuilder.Entity<Pedido>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ClienteId).IsRequired();
            entity.Property(e => e.RestauranteId).IsRequired();
            entity.Property(e => e.Codigo).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Estado).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Subtotal).HasPrecision(18, 2);
            entity.Property(e => e.CostoEnvio).HasPrecision(18, 2);
            entity.Property(e => e.Total).HasPrecision(18, 2);
            entity.Property(e => e.DireccionEntregaId).IsRequired();
            entity.Property(e => e.QrCodigo).IsRequired().HasMaxLength(36);
            entity.Property(e => e.NotasCliente).HasMaxLength(500);
            entity.Property(e => e.NotasRestaurante).HasMaxLength(500);
            entity.Property(e => e.FechaCreacion).IsRequired();

            entity.HasIndex(e => e.ClienteId);
            entity.HasIndex(e => e.RestauranteId);
            entity.HasIndex(e => e.Estado);
            entity.HasIndex(e => e.Codigo).IsUnique();

            entity.HasOne(e => e.DireccionEntrega)
                .WithMany(d => d.Pedidos)
                .HasForeignKey(e => e.DireccionEntregaId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Restaurante)
                .WithMany()
                .HasForeignKey(e => e.RestauranteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.Items)
                .WithOne(i => i.Pedido)
                .HasForeignKey(i => i.PedidoId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Contactos)
                .WithOne(c => c.Pedido)
                .HasForeignKey(c => c.PedidoId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Tarifa)
                .WithOne(t => t.Pedido)
                .HasForeignKey<TarifaAplicada>(t => t.PedidoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ItemPedido
        modelBuilder.Entity<ItemPedido>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PedidoId).IsRequired();
            entity.Property(e => e.NombreProducto).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Descripcion).HasMaxLength(500);
            entity.Property(e => e.Cantidad).IsRequired();
            entity.Property(e => e.PrecioUnitario).HasPrecision(18, 2);
            entity.Property(e => e.Subtotal).HasPrecision(18, 2);

            entity.HasIndex(e => e.PedidoId);
        });

        // DireccionEntrega
        modelBuilder.Entity<DireccionEntrega>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Calle).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Numero).HasMaxLength(100);
            entity.Property(e => e.Interior).HasMaxLength(100);
            entity.Property(e => e.Referencia).HasMaxLength(100);
            entity.Property(e => e.Ciudad).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Departamento).HasMaxLength(100);
            entity.Property(e => e.CodigoPostal).HasMaxLength(20);
            entity.Property(e => e.Ubicacion).HasColumnType("geography (point)").IsRequired();

            entity.HasIndex(e => e.Ubicacion).HasMethod("GIST");
        });

        // ContactoPedido
        modelBuilder.Entity<ContactoPedido>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PedidoId).IsRequired();
            entity.Property(e => e.Tipo).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Nombre).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Telefono).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Email).HasMaxLength(256);

            entity.HasIndex(e => e.PedidoId);
        });

        // TarifaAplicada
        modelBuilder.Entity<TarifaAplicada>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PedidoId).IsRequired();
            entity.Property(e => e.TipoTarifa).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CostoBase).HasPrecision(18, 2);
            entity.Property(e => e.CostoPorKm).HasPrecision(18, 2);
            entity.Property(e => e.DistanciaKm).HasPrecision(10, 2);
            entity.Property(e => e.CostoAdicional).HasPrecision(18, 2);
            entity.Property(e => e.DetalleCalculo).HasMaxLength(200);
            entity.Property(e => e.TotalCalculado).HasPrecision(18, 2);

            entity.HasIndex(e => e.PedidoId).IsUnique();
        });

        // RestauranteReplicado
        modelBuilder.Entity<RestauranteReplicado>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.NombreComercial).IsRequired().HasMaxLength(200);
            entity.Property(e => e.RazonSocial).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Nit).IsRequired().HasMaxLength(50);
            entity.Property(e => e.DireccionSede).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Ciudad).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Latitud).HasColumnType("decimal(10,8)").IsRequired();
            entity.Property(e => e.Longitud).HasColumnType("decimal(11,8)").IsRequired();
            entity.Property(e => e.Ubicacion).HasColumnType("geography (point)").IsRequired();
            entity.Property(e => e.Distrito).HasMaxLength(100);
            entity.Property(e => e.Provincia).HasMaxLength(100);
            entity.Property(e => e.Departamento).HasMaxLength(100);
            entity.Property(e => e.RadioCoberturaKm).HasColumnType("decimal(10,2)").IsRequired();
            entity.Property(e => e.AceptaPedidos).IsRequired();
            entity.Property(e => e.Activo).IsRequired();
            entity.Property(e => e.HoraApertura).IsRequired();
            entity.Property(e => e.HoraCierre).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => e.Ubicacion).HasMethod("GIST");
            entity.HasIndex(e => e.Nit).IsUnique();
            entity.HasIndex(e => e.Ciudad);
            entity.HasIndex(e => e.Activo);

            entity.HasMany(e => e.ZonasCobertura)
                .WithOne(z => z.RestauranteReplicado)
                .HasForeignKey(z => z.RestauranteReplicadoId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.HorariosAtencion)
                .WithOne(h => h.RestauranteReplicado)
                .HasForeignKey(h => h.RestauranteReplicadoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ZonaCoberturaReplicada
        modelBuilder.Entity<ZonaCoberturaReplicada>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RestauranteReplicadoId).IsRequired();
            entity.Property(e => e.NombreZona).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Descripcion).HasMaxLength(200);
            entity.Property(e => e.CostoEnvio).HasColumnType("decimal(10,2)");
            entity.Property(e => e.PedidoMinimo).HasColumnType("decimal(10,2)");
            entity.Property(e => e.TiempoEstimadoMinutos).IsRequired();
            entity.Property(e => e.Activo).IsRequired();
            entity.Property(e => e.Geom).HasColumnType("geography (polygon)");

            entity.HasIndex(e => e.RestauranteReplicadoId);
            entity.HasIndex(e => e.Geom).HasMethod("GIST");
        });

        // HorarioAtencionReplicado
        modelBuilder.Entity<HorarioAtencionReplicado>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RestauranteReplicadoId).IsRequired();
            entity.Property(e => e.DiaSemana).IsRequired().HasMaxLength(20);
            entity.Property(e => e.HoraInicio).IsRequired();
            entity.Property(e => e.HoraFin).IsRequired();
            entity.Property(e => e.Cerrado).IsRequired();

            entity.HasIndex(e => e.RestauranteReplicadoId);
        });
    }
}