using Microsoft.EntityFrameworkCore;
using Deuna.Identity.Service.Models.Domain;

namespace Deuna.Identity.Service.Models;

public class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }

    // Entidades principales
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<PerfilRestaurante> PerfilesRestaurante => Set<PerfilRestaurante>();
    public DbSet<PerfilRepartidor> PerfilesRepartidor => Set<PerfilRepartidor>();
    public DbSet<PerfilAdministrador> PerfilesAdministrador => Set<PerfilAdministrador>();

    // Subtablas
    public DbSet<HorarioAtencion> HorariosAtencion => Set<HorarioAtencion>();
    public DbSet<ZonaCoberturaRestaurante> ZonasCoberturaRestaurante => Set<ZonaCoberturaRestaurante>();
    public DbSet<MetodoPagoRestaurante> MetodosPagoRestaurante => Set<MetodoPagoRestaurante>();

    public DbSet<VehiculoRepartidor> VehiculosRepartidor => Set<VehiculoRepartidor>();
    public DbSet<ZonaCoberturaRepartidor> ZonasCoberturaRepartidor => Set<ZonaCoberturaRepartidor>();
    public DbSet<DocumentoRepartidor> DocumentosRepartidor => Set<DocumentoRepartidor>();

    public DbSet<PermisoAdministrador> PermisosAdministrador => Set<PermisoAdministrador>();
    public DbSet<AuditoriaAdministrador> AuditoriasAdministrador => Set<AuditoriaAdministrador>();

    // Auth
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Skip relational-specific configuration for InMemory database
        if (Database.IsInMemory())
        {
            return;
        }

        // ========== USUARIO ==========
        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.ToTable("usuarios");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt);
            entity.Property(e => e.LastLoginAt);
            entity.Property(e => e.DeletedAt);
            entity.HasIndex(e => e.Email).IsUnique().HasDatabaseName("ix_usuarios_email");
            entity.HasIndex(e => e.PhoneNumber).IsUnique().HasDatabaseName("ix_usuarios_phone").HasFilter("\"PhoneNumber\" IS NOT NULL");
            entity.HasIndex(e => e.IsActive).HasDatabaseName("ix_usuarios_active");
            entity.HasIndex(e => e.DeletedAt).HasDatabaseName("ix_usuarios_deleted");

            // Relaciones 1:1 con perfiles
            entity.HasOne(e => e.PerfilRestaurante)
                .WithOne(p => p.Usuario)
                .HasForeignKey<PerfilRestaurante>(p => p.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.PerfilRepartidor)
                .WithOne(p => p.Usuario)
                .HasForeignKey<PerfilRepartidor>(p => p.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.PerfilAdministrador)
                .WithOne(p => p.Usuario)
                .HasForeignKey<PerfilAdministrador>(p => p.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ========== PERFIL RESTAURANTE ==========
        modelBuilder.Entity<PerfilRestaurante>(entity =>
        {
            entity.ToTable("perfiles_restaurante");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UsuarioId).IsRequired();
            entity.Property(e => e.NombreComercial).IsRequired().HasMaxLength(200);
            entity.Property(e => e.RazonSocial).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Nit).IsRequired().HasMaxLength(50);
            entity.Property(e => e.DireccionSede).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Ciudad).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Latitud).IsRequired().HasColumnType("decimal(10,8)");
            entity.Property(e => e.Longitud).IsRequired().HasColumnType("decimal(11,8)");
            entity.Property(e => e.Distrito).HasMaxLength(100);
            entity.Property(e => e.Provincia).HasMaxLength(100);
            entity.Property(e => e.Departamento).HasMaxLength(100);
            entity.Property(e => e.CodigoPostal).HasMaxLength(10);
            entity.Property(e => e.Telefono).HasMaxLength(20);
            entity.Property(e => e.EmailContacto).HasMaxLength(200);
            entity.Property(e => e.Descripcion).HasMaxLength(500);
            entity.Property(e => e.LogoUrl).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt);
            entity.HasIndex(e => e.UsuarioId).IsUnique().HasDatabaseName("ix_perfiles_restaurante_usuario");
            entity.HasIndex(e => e.Nit).IsUnique().HasDatabaseName("ix_perfiles_restaurante_nit");
            entity.HasIndex(e => e.Activo).HasDatabaseName("ix_perfiles_restaurante_activo");

            // Subtablas
            entity.HasMany(e => e.HorariosAtencion)
                .WithOne(h => h.PerfilRestaurante)
                .HasForeignKey(h => h.PerfilRestauranteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.ZonasCobertura)
                .WithOne(z => z.PerfilRestaurante)
                .HasForeignKey(z => z.PerfilRestauranteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.MetodosPago)
                .WithOne(m => m.PerfilRestaurante)
                .HasForeignKey(m => m.PerfilRestauranteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HorarioAtencion>(entity =>
        {
            entity.ToTable("horarios_atencion");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PerfilRestauranteId).IsRequired();
            entity.Property(e => e.DiaSemana).IsRequired().HasMaxLength(20);
            entity.Property(e => e.HoraInicio).IsRequired();
            entity.Property(e => e.HoraFin).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.HasIndex(e => e.PerfilRestauranteId).HasDatabaseName("ix_horarios_restaurante");
            entity.HasIndex(e => new { e.PerfilRestauranteId, e.DiaSemana }).IsUnique().HasDatabaseName("ix_horarios_restaurante_dia");
        });

        modelBuilder.Entity<ZonaCoberturaRestaurante>(entity =>
        {
            entity.ToTable("zonas_cobertura_restaurante");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PerfilRestauranteId).IsRequired();
            entity.Property(e => e.NombreZona).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Descripcion).HasMaxLength(200);
            entity.Property(e => e.CostoEnvio).HasColumnType("decimal(10,2)");
            entity.Property(e => e.PedidoMinimo).HasColumnType("decimal(10,2)");
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.HasIndex(e => e.PerfilRestauranteId).HasDatabaseName("ix_zonas_cob_restaurante");
            entity.HasIndex(e => new { e.PerfilRestauranteId, e.NombreZona }).IsUnique().HasDatabaseName("ix_zonas_cob_restaurante_nombre");
        });

        modelBuilder.Entity<MetodoPagoRestaurante>(entity =>
        {
            entity.ToTable("metodos_pago_restaurante");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PerfilRestauranteId).IsRequired();
            entity.Property(e => e.Tipo).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Proveedor).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.HasIndex(e => e.PerfilRestauranteId).HasDatabaseName("ix_metodos_pago_restaurante");
        });

        // ========== PERFIL REPARTIDOR ==========
        modelBuilder.Entity<PerfilRepartidor>(entity =>
        {
            entity.ToTable("perfiles_repartidor");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UsuarioId).IsRequired();
            entity.Property(e => e.NumeroLicencia).HasMaxLength(20);
            entity.Property(e => e.TipoLicencia).HasMaxLength(100);
            entity.Property(e => e.FotoPerfilUrl).HasMaxLength(500);
            entity.Property(e => e.CalificacionPromedio).HasColumnType("decimal(3,2)");
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt);
            entity.HasIndex(e => e.UsuarioId).IsUnique().HasDatabaseName("ix_perfiles_repartidor_usuario");
            entity.HasIndex(e => e.NumeroLicencia).IsUnique().HasDatabaseName("ix_perfiles_repartidor_licencia").HasFilter("\"NumeroLicencia\" IS NOT NULL");
            entity.HasIndex(e => e.Activo).HasDatabaseName("ix_perfiles_repartidor_activo");
            entity.HasIndex(e => e.Disponible).HasDatabaseName("ix_perfiles_repartidor_disponible");

            entity.HasMany(e => e.Vehiculos)
                .WithOne(v => v.PerfilRepartidor)
                .HasForeignKey(v => v.PerfilRepartidorId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.ZonasCobertura)
                .WithOne(z => z.PerfilRepartidor)
                .HasForeignKey(z => z.PerfilRepartidorId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Documentos)
                .WithOne(d => d.PerfilRepartidor)
                .HasForeignKey(d => d.PerfilRepartidorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VehiculoRepartidor>(entity =>
        {
            entity.ToTable("vehiculos_repartidor");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PerfilRepartidorId).IsRequired();
            entity.Property(e => e.Tipo).IsRequired();
            entity.Property(e => e.Marca).HasMaxLength(50);
            entity.Property(e => e.Modelo).HasMaxLength(50);
            entity.Property(e => e.Placa).HasMaxLength(20);
            entity.Property(e => e.Color).HasMaxLength(20);
            entity.Property(e => e.NumeroSOAT).HasMaxLength(100);
            entity.Property(e => e.NumeroTarjetaPropiedad).HasMaxLength(100);
            entity.Property(e => e.Estado).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt);
            entity.HasIndex(e => e.PerfilRepartidorId).HasDatabaseName("ix_vehiculos_repartidor");
            entity.HasIndex(e => e.Placa).IsUnique().HasDatabaseName("ix_vehiculos_placa").HasFilter("\"Placa\" IS NOT NULL");
        });

        modelBuilder.Entity<ZonaCoberturaRepartidor>(entity =>
        {
            entity.ToTable("zonas_cobertura_repartidor");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PerfilRepartidorId).IsRequired();
            entity.Property(e => e.NombreZona).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Descripcion).HasMaxLength(200);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.HasIndex(e => e.PerfilRepartidorId).HasDatabaseName("ix_zonas_cob_repartidor");
        });

        modelBuilder.Entity<DocumentoRepartidor>(entity =>
        {
            entity.ToTable("documentos_repartidor");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PerfilRepartidorId).IsRequired();
            entity.Property(e => e.Tipo).IsRequired().HasMaxLength(100);
            entity.Property(e => e.UrlArchivo).HasMaxLength(500);
            entity.Property(e => e.NumeroDocumento).HasMaxLength(200);
            entity.Property(e => e.Estado).IsRequired();
            entity.Property(e => e.Observaciones).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt);
            entity.HasIndex(e => e.PerfilRepartidorId).HasDatabaseName("ix_documentos_repartidor");
            entity.HasIndex(e => new { e.PerfilRepartidorId, e.Tipo }).HasDatabaseName("ix_documentos_repartidor_tipo");
        });

        // ========== PERFIL ADMINISTRADOR ==========
        modelBuilder.Entity<PerfilAdministrador>(entity =>
        {
            entity.ToTable("perfiles_administrador");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UsuarioId).IsRequired();
            entity.Property(e => e.Cargo).HasMaxLength(100);
            entity.Property(e => e.Departamento).HasMaxLength(200);
            entity.Property(e => e.JefeDirectoId).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt);
            entity.Property(e => e.UltimoAcceso);
            entity.HasIndex(e => e.UsuarioId).IsUnique().HasDatabaseName("ix_perfiles_admin_usuario");
            entity.HasIndex(e => e.Activo).HasDatabaseName("ix_perfiles_admin_activo");

            entity.HasMany(e => e.Permisos)
                .WithOne(p => p.PerfilAdministrador)
                .HasForeignKey(p => p.PerfilAdministradorId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Auditorias)
                .WithOne(a => a.PerfilAdministrador)
                .HasForeignKey(a => a.PerfilAdministradorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PermisoAdministrador>(entity =>
        {
            entity.ToTable("permisos_administrador");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PerfilAdministradorId).IsRequired();
            entity.Property(e => e.Permiso).IsRequired();
            entity.Property(e => e.Recurso).HasMaxLength(100);
            entity.Property(e => e.Accion).HasMaxLength(50);
            entity.Property(e => e.Observaciones).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt);
            entity.HasIndex(e => e.PerfilAdministradorId).HasDatabaseName("ix_permisos_admin");
            entity.HasIndex(e => new { e.PerfilAdministradorId, e.Permiso, e.Recurso }).IsUnique().HasDatabaseName("ix_permisos_admin_unique");
        });

        modelBuilder.Entity<AuditoriaAdministrador>(entity =>
        {
            entity.ToTable("auditoria_administrador");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PerfilAdministradorId).IsRequired();
            entity.Property(e => e.Accion).IsRequired().HasMaxLength(100);
            entity.Property(e => e.EntidadAfectada).HasMaxLength(100);
            entity.Property(e => e.Detalle).HasMaxLength(2000);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.ErrorMessage).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.HasIndex(e => e.PerfilAdministradorId).HasDatabaseName("ix_auditoria_admin");
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("ix_auditoria_admin_fecha");
            entity.HasIndex(e => e.Accion).HasDatabaseName("ix_auditoria_admin_accion");
        });

        // ========== REFRESH TOKENS ==========
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).IsRequired().HasMaxLength(500);
            entity.Property(e => e.UsuarioId).IsRequired();
            entity.Property(e => e.ExpiresAt).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.RevokedAt);
            entity.Property(e => e.CreatedByIp).HasMaxLength(45);
            entity.Property(e => e.RevokedByIp).HasMaxLength(45);
            entity.Property(e => e.ReplacedByToken).HasMaxLength(500);
            entity.HasIndex(e => e.Token).IsUnique().HasDatabaseName("ix_refresh_tokens_token");
            entity.HasIndex(e => e.UsuarioId).HasDatabaseName("ix_refresh_tokens_usuario");
            entity.HasIndex(e => e.ExpiresAt).HasDatabaseName("ix_refresh_tokens_expires");
            entity.HasOne(e => e.Usuario)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(e => e.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}