using System.ComponentModel.DataAnnotations;
using Deuna.Identity.Service.Models.Domain;

namespace Deuna.Identity.Service.DTOs;

public record PerfilRestauranteRequest(
    [Required, MaxLength(200)] string NombreComercial,
    [MaxLength(200)] string? RazonSocial,
    [MaxLength(20)] string? Ruc,
    [MaxLength(500)] string? Direccion,
    [MaxLength(100)] string? Distrito,
    [MaxLength(100)] string? Provincia,
    [MaxLength(100)] string? Departamento,
    [MaxLength(10)] string? CodigoPostal,
    [MaxLength(20), Phone] string? Telefono,
    [MaxLength(200), EmailAddress] string? EmailContacto,
    [MaxLength(500)] string? Descripcion,
    [MaxLength(500)] string? LogoUrl,
    TimeSpan? HoraApertura,
    TimeSpan? HoraCierre,
    bool AceptaPedidos = true
);

public record PerfilRestauranteResponse(
    Guid Id,
    Guid UsuarioId,
    string NombreComercial,
    string? RazonSocial,
    string? Ruc,
    string? Direccion,
    string? Distrito,
    string? Provincia,
    string? Departamento,
    string? CodigoPostal,
    string? Telefono,
    string? EmailContacto,
    string? Descripcion,
    string? LogoUrl,
    bool AceptaPedidos,
    bool Activo,
    TimeSpan HoraApertura,
    TimeSpan HoraCierre,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<HorarioAtencionResponse> HorariosAtencion,
    List<ZonaCoberturaRestauranteResponse> ZonasCobertura,
    List<MetodoPagoRestauranteResponse> MetodosPago
);

public record HorarioAtencionRequest(
    [Required, MaxLength(20)] string DiaSemana,
    [Required] TimeSpan HoraInicio,
    [Required] TimeSpan HoraFin,
    bool Cerrado = false
);

public record HorarioAtencionResponse(
    Guid Id,
    Guid PerfilRestauranteId,
    string DiaSemana,
    TimeSpan HoraInicio,
    TimeSpan HoraFin,
    bool Cerrado,
    DateTime CreatedAt
);

public record ZonaCoberturaRestauranteRequest(
    [Required, MaxLength(100)] string NombreZona,
    [MaxLength(200)] string? Descripcion,
    [Range(0, double.MaxValue)] decimal CostoEnvio = 0,
    [Range(0, double.MaxValue)] decimal PedidoMinimo = 0,
    [Range(1, 480)] int TiempoEstimadoMinutos = 30,
    bool Activo = true
);

public record ZonaCoberturaRestauranteResponse(
    Guid Id,
    Guid PerfilRestauranteId,
    string NombreZona,
    string? Descripcion,
    decimal CostoEnvio,
    decimal PedidoMinimo,
    int TiempoEstimadoMinutos,
    bool Activo,
    DateTime CreatedAt
);

public record MetodoPagoRestauranteRequest(
    [Required, MaxLength(50)] string Tipo,
    [MaxLength(100)] string? Proveedor,
    bool Activo = true,
    bool EsPredeterminado = false
);

public record MetodoPagoRestauranteResponse(
    Guid Id,
    Guid PerfilRestauranteId,
    string Tipo,
    string? Proveedor,
    bool Activo,
    bool EsPredeterminado,
    DateTime CreatedAt
);

// ========== REPARTIDOR ==========
public record PerfilRepartidorRequest(
    [MaxLength(20)] string? NumeroLicencia,
    [MaxLength(100)] string? TipoLicencia,
    DateTime? FechaVencimientoLicencia,
    [MaxLength(500)] string? FotoPerfilUrl,
    bool Disponible = true
);

public record PerfilRepartidorResponse(
    Guid Id,
    Guid UsuarioId,
    string? NumeroLicencia,
    string? TipoLicencia,
    DateTime? FechaVencimientoLicencia,
    string? FotoPerfilUrl,
    bool Disponible,
    bool Activo,
    decimal CalificacionPromedio,
    int TotalEntregas,
    int EntregasCompletadas,
    int EntregasCanceladas,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<VehiculoRepartidorResponse> Vehiculos,
    List<ZonaCoberturaRepartidorResponse> ZonasCobertura,
    List<DocumentoRepartidorResponse> Documentos
);

public record VehiculoRepartidorRequest(
    [Required] TipoVehiculo Tipo,
    [MaxLength(50)] string? Marca,
    [MaxLength(50)] string? Modelo,
    [MaxLength(20)] string? Placa,
    [MaxLength(20)] string? Color,
    int? AnioFabricacion,
    [MaxLength(100)] string? NumeroSOAT,
    DateTime? FechaVencimientoSOAT,
    [MaxLength(100)] string? NumeroTarjetaPropiedad,
    bool EsPrincipal = false,
    EstadoVehiculo Estado = EstadoVehiculo.ACTIVO
);

public record VehiculoRepartidorResponse(
    Guid Id,
    Guid PerfilRepartidorId,
    TipoVehiculo Tipo,
    string? Marca,
    string? Modelo,
    string? Placa,
    string? Color,
    int? AnioFabricacion,
    string? NumeroSOAT,
    DateTime? FechaVencimientoSOAT,
    string? NumeroTarjetaPropiedad,
    EstadoVehiculo Estado,
    bool EsPrincipal,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record ZonaCoberturaRepartidorRequest(
    [Required, MaxLength(100)] string NombreZona,
    [MaxLength(200)] string? Descripcion,
    bool Activo = true
);

public record ZonaCoberturaRepartidorResponse(
    Guid Id,
    Guid PerfilRepartidorId,
    string NombreZona,
    string? Descripcion,
    bool Activo,
    DateTime CreatedAt
);

public record DocumentoRepartidorRequest(
    [Required, MaxLength(100)] string Tipo,
    [MaxLength(500)] string? UrlArchivo,
    [MaxLength(200)] string? NumeroDocumento,
    DateTime? FechaEmision,
    DateTime? FechaVencimiento,
    [MaxLength(500)] string? Observaciones,
    EstadoDocumento Estado = EstadoDocumento.PENDIENTE
);

public record DocumentoRepartidorResponse(
    Guid Id,
    Guid PerfilRepartidorId,
    string Tipo,
    string? UrlArchivo,
    string? NumeroDocumento,
    DateTime? FechaEmision,
    DateTime? FechaVencimiento,
    EstadoDocumento Estado,
    string? Observaciones,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

// ========== ADMINISTRADOR ==========
public record PerfilAdministradorRequest(
    [MaxLength(100)] string? Cargo,
    [MaxLength(200)] string? Departamento,
    [MaxLength(100)] string? JefeDirectoId,
    bool EsSuperAdmin = false
);

public record PerfilAdministradorResponse(
    Guid Id,
    Guid UsuarioId,
    string? Cargo,
    string? Departamento,
    string? JefeDirectoId,
    bool EsSuperAdmin,
    bool Activo,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? UltimoAcceso,
    List<PermisoAdministradorResponse> Permisos,
    List<AuditoriaAdministradorResponse> Auditorias
);

public record PermisoAdministradorRequest(
    [Required] TipoPermiso Permiso,
    [MaxLength(100)] string? Recurso = null,
    [MaxLength(50)] string? Accion = null,
    bool Concedido = true,
    DateTime? FechaInicio = null,
    DateTime? FechaFin = null,
    [MaxLength(500)] string? Observaciones = null
);

public record PermisoAdministradorResponse(
    Guid Id,
    Guid PerfilAdministradorId,
    TipoPermiso Permiso,
    string? Recurso,
    string? Accion,
    bool Concedido,
    DateTime? FechaInicio,
    DateTime? FechaFin,
    string? Observaciones,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record AuditoriaAdministradorResponse(
    Guid Id,
    Guid PerfilAdministradorId,
    string Accion,
    string? EntidadAfectada,
    Guid? EntidadId,
    string? Detalle,
    string? IpAddress,
    string? UserAgent,
    bool Exitoso,
    string? ErrorMessage,
    DateTime CreatedAt
);

// ========== UPLOAD DOCUMENTOS ==========
public record UploadDocumentoRequest(
    [Required] IFormFile File,
    [Required, MaxLength(100)] string Tipo,
    [MaxLength(200)] string? NumeroDocumento,
    DateTime? FechaEmision,
    DateTime? FechaVencimiento,
    [MaxLength(500)] string? Observaciones
);

public record UploadDocumentoResponse(
    Guid Id,
    string UrlArchivo,
    string Tipo,
    string? NumeroDocumento,
    DateTime? FechaEmision,
    DateTime? FechaVencimiento,
    EstadoDocumento Estado,
    string? Observaciones,
    DateTime CreatedAt
);