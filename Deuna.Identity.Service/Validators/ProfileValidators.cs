using FluentValidation;
using Deuna.Identity.Service.DTOs;
using Deuna.Identity.Service.Models.Domain;

namespace Deuna.Identity.Service.Validators;

public class PerfilRestauranteRequestValidator : AbstractValidator<PerfilRestauranteRequest>
{
    public PerfilRestauranteRequestValidator()
    {
        RuleFor(x => x.NombreComercial)
            .NotEmpty().WithMessage("Nombre comercial es requerido")
            .MaximumLength(200).WithMessage("Nombre comercial demasiado largo");

        RuleFor(x => x.RazonSocial)
            .MaximumLength(200).When(x => !string.IsNullOrEmpty(x.RazonSocial))
            .WithMessage("Razón social demasiado larga");

        RuleFor(x => x.Ruc)
            .Matches(@"^\d{11}$").When(x => !string.IsNullOrEmpty(x.Ruc))
            .WithMessage("RUC debe tener 11 dígitos");

        RuleFor(x => x.Direccion)
            .MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Direccion))
            .WithMessage("Dirección demasiado larga");

        RuleFor(x => x.Distrito)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.Distrito))
            .WithMessage("Distrito demasiado largo");

        RuleFor(x => x.Provincia)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.Provincia))
            .WithMessage("Provincia demasiado larga");

        RuleFor(x => x.Departamento)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.Departamento))
            .WithMessage("Departamento demasiado largo");

        RuleFor(x => x.CodigoPostal)
            .MaximumLength(10).When(x => !string.IsNullOrEmpty(x.CodigoPostal))
            .WithMessage("Código postal demasiado largo");

        RuleFor(x => x.Telefono)
            .Matches(@"^\+?[1-9]\d{1,14}$").When(x => !string.IsNullOrEmpty(x.Telefono))
            .WithMessage("Teléfono inválido");

        RuleFor(x => x.EmailContacto)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.EmailContacto))
            .WithMessage("Email de contacto inválido")
            .MaximumLength(200).When(x => !string.IsNullOrEmpty(x.EmailContacto))
            .WithMessage("Email demasiado largo");

        RuleFor(x => x.Descripcion)
            .MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Descripcion))
            .WithMessage("Descripción demasiado larga");

        RuleFor(x => x.LogoUrl)
            .Must(BeValidUrl).When(x => !string.IsNullOrEmpty(x.LogoUrl))
            .WithMessage("Logo URL inválida");

        RuleFor(x => x.HoraApertura)
            .LessThan(x => x.HoraCierre).When(x => x.HoraApertura.HasValue && x.HoraCierre.HasValue)
            .WithMessage("Hora de apertura debe ser anterior a hora de cierre");
    }

    private bool BeValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out _);
    }
}

public class HorarioAtencionRequestValidator : AbstractValidator<HorarioAtencionRequest>
{
    public HorarioAtencionRequestValidator()
    {
        RuleFor(x => x.DiaSemana)
            .NotEmpty().WithMessage("Día de la semana es requerido")
            .Must(BeValidDay).WithMessage("Día de semana inválido")
            .MaximumLength(20).WithMessage("Día de semana demasiado largo");

        RuleFor(x => x.HoraFin)
            .GreaterThan(x => x.HoraInicio).When(x => !x.Cerrado)
            .WithMessage("Hora de fin debe ser posterior a hora de inicio");
    }

    private bool BeValidDay(string dia)
    {
        var validDays = new[] { "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo",
            "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        return validDays.Contains(dia, StringComparer.OrdinalIgnoreCase);
    }
}

public class ZonaCoberturaRestauranteRequestValidator : AbstractValidator<ZonaCoberturaRestauranteRequest>
{
    public ZonaCoberturaRestauranteRequestValidator()
    {
        RuleFor(x => x.NombreZona)
            .NotEmpty().WithMessage("Nombre de zona es requerido")
            .MaximumLength(100).WithMessage("Nombre de zona demasiado largo");

        RuleFor(x => x.Descripcion)
            .MaximumLength(200).When(x => !string.IsNullOrEmpty(x.Descripcion))
            .WithMessage("Descripción demasiado larga");

        RuleFor(x => x.CostoEnvio)
            .GreaterThanOrEqualTo(0).WithMessage("Costo de envío no puede ser negativo");

        RuleFor(x => x.PedidoMinimo)
            .GreaterThanOrEqualTo(0).WithMessage("Pedido mínimo no puede ser negativo");

        RuleFor(x => x.TiempoEstimadoMinutos)
            .InclusiveBetween(1, 480).WithMessage("Tiempo estimado debe ser entre 1 y 480 minutos");
    }
}

public class MetodoPagoRestauranteRequestValidator : AbstractValidator<MetodoPagoRestauranteRequest>
{
    public MetodoPagoRestauranteRequestValidator()
    {
        RuleFor(x => x.Tipo)
            .NotEmpty().WithMessage("Tipo de pago es requerido")
            .Must(BeValidTipo).WithMessage("Tipo de pago inválido. Valores: EFECTIVO, TARJETA, YAPE, PLIN, TRANSFERENCIA")
            .MaximumLength(50).WithMessage("Tipo demasiado largo");

        RuleFor(x => x.Proveedor)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.Proveedor))
            .WithMessage("Proveedor demasiado largo");
    }

    private bool BeValidTipo(string tipo)
    {
        var valid = new[] { "EFECTIVO", "TARJETA", "YAPE", "PLIN", "TRANSFERENCIA" };
        return valid.Contains(tipo.ToUpperInvariant());
    }
}

// ========== REPARTIDOR ==========
public class PerfilRepartidorRequestValidator : AbstractValidator<PerfilRepartidorRequest>
{
    public PerfilRepartidorRequestValidator()
    {
        RuleFor(x => x.NumeroLicencia)
            .Matches(@"^[A-Z0-9]{6,20}$").When(x => !string.IsNullOrEmpty(x.NumeroLicencia))
            .WithMessage("Número de licencia inválido (6-20 caracteres alfanuméricos)");

        RuleFor(x => x.TipoLicencia)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.TipoLicencia))
            .WithMessage("Tipo de licencia demasiado largo");

        RuleFor(x => x.FechaVencimientoLicencia)
            .GreaterThan(DateTime.UtcNow).When(x => x.FechaVencimientoLicencia.HasValue)
            .WithMessage("Fecha de vencimiento de licencia debe ser futura");

        RuleFor(x => x.FotoPerfilUrl)
            .Must(BeValidUrl).When(x => !string.IsNullOrEmpty(x.FotoPerfilUrl))
            .WithMessage("URL de foto inválida");
    }

    private bool BeValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out _);
    }
}

public class VehiculoRepartidorRequestValidator : AbstractValidator<VehiculoRepartidorRequest>
{
    public VehiculoRepartidorRequestValidator()
    {
        RuleFor(x => x.Tipo)
            .IsInEnum().WithMessage("Tipo de vehículo inválido");

        RuleFor(x => x.Marca)
            .MaximumLength(50).When(x => !string.IsNullOrEmpty(x.Marca))
            .WithMessage("Marca demasiado larga");

        RuleFor(x => x.Modelo)
            .MaximumLength(50).When(x => !string.IsNullOrEmpty(x.Modelo))
            .WithMessage("Modelo demasiado largo");

        RuleFor(x => x.Placa)
            .Matches(@"^[A-Z]{3}-\d{3}$").When(x => !string.IsNullOrEmpty(x.Placa))
            .WithMessage("Placa inválida. Formato: ABC-123");

        RuleFor(x => x.Color)
            .MaximumLength(20).When(x => !string.IsNullOrEmpty(x.Color))
            .WithMessage("Color demasiado largo");

        RuleFor(x => x.AnioFabricacion)
            .InclusiveBetween(1990, DateTime.UtcNow.Year + 1).When(x => x.AnioFabricacion.HasValue)
            .WithMessage($"Año de fabricación debe ser entre 1990 y {DateTime.UtcNow.Year + 1}");

        RuleFor(x => x.NumeroSOAT)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.NumeroSOAT))
            .WithMessage("Número SOAT demasiado largo");

        RuleFor(x => x.FechaVencimientoSOAT)
            .GreaterThan(DateTime.UtcNow).When(x => x.FechaVencimientoSOAT.HasValue)
            .WithMessage("Fecha de vencimiento SOAT debe ser futura");

        RuleFor(x => x.NumeroTarjetaPropiedad)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.NumeroTarjetaPropiedad))
            .WithMessage("Número tarjeta propiedad demasiado largo");
    }
}

public class ZonaCoberturaRepartidorRequestValidator : AbstractValidator<ZonaCoberturaRepartidorRequest>
{
    public ZonaCoberturaRepartidorRequestValidator()
    {
        RuleFor(x => x.NombreZona)
            .NotEmpty().WithMessage("Nombre de zona es requerido")
            .MaximumLength(100).WithMessage("Nombre de zona demasiado largo");

        RuleFor(x => x.Descripcion)
            .MaximumLength(200).When(x => !string.IsNullOrEmpty(x.Descripcion))
            .WithMessage("Descripción demasiado larga");
    }
}

public class DocumentoRepartidorRequestValidator : AbstractValidator<DocumentoRepartidorRequest>
{
    public DocumentoRepartidorRequestValidator()
    {
        RuleFor(x => x.Tipo)
            .NotEmpty().WithMessage("Tipo de documento es requerido")
            .Must(BeValidTipo).WithMessage("Tipo inválido. Valores: LICENCIA, SOAT, TARJETA_PROPIEDAD, DNI, ANTECEDENTES")
            .MaximumLength(100).WithMessage("Tipo demasiado largo");

        RuleFor(x => x.UrlArchivo)
            .NotEmpty().WithMessage("URL del archivo es requerida")
            .Must(BeValidUrl).WithMessage("URL inválida")
            .MaximumLength(500).WithMessage("URL demasiado larga");

        RuleFor(x => x.NumeroDocumento)
            .MaximumLength(200).When(x => !string.IsNullOrEmpty(x.NumeroDocumento))
            .WithMessage("Número de documento demasiado largo");

        RuleFor(x => x.FechaEmision)
            .LessThanOrEqualTo(DateTime.UtcNow).When(x => x.FechaEmision.HasValue)
            .WithMessage("Fecha de emisión no puede ser futura");

        RuleFor(x => x.FechaVencimiento)
            .GreaterThan(x => x.FechaEmision).When(x => x.FechaVencimiento.HasValue && x.FechaEmision.HasValue)
            .WithMessage("Fecha de vencimiento debe ser posterior a fecha de emisión");

        RuleFor(x => x.Estado)
            .IsInEnum().WithMessage("Estado de documento inválido");

        RuleFor(x => x.Observaciones)
            .MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Observaciones))
            .WithMessage("Observaciones demasiado largas");
    }

    private bool BeValidTipo(string tipo)
    {
        var valid = new[] { "LICENCIA", "SOAT", "TARJETA_PROPIEDAD", "DNI", "ANTECEDENTES" };
        return valid.Contains(tipo.ToUpperInvariant());
    }

    private bool BeValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out _);
    }
}

// ========== ADMINISTRADOR ==========
public class PerfilAdministradorRequestValidator : AbstractValidator<PerfilAdministradorRequest>
{
    public PerfilAdministradorRequestValidator()
    {
        RuleFor(x => x.Cargo)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.Cargo))
            .WithMessage("Cargo demasiado largo");

        RuleFor(x => x.Departamento)
            .MaximumLength(200).When(x => !string.IsNullOrEmpty(x.Departamento))
            .WithMessage("Departamento demasiado largo");

        RuleFor(x => x.JefeDirectoId)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.JefeDirectoId))
            .WithMessage("Jefe directo ID demasiado largo");
    }
}

public class PermisoAdministradorRequestValidator : AbstractValidator<PermisoAdministradorRequest>
{
    public PermisoAdministradorRequestValidator()
    {
        RuleFor(x => x.Permiso)
            .IsInEnum().WithMessage("Tipo de permiso inválido");

        RuleFor(x => x.Recurso)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.Recurso))
            .WithMessage("Recurso demasiado largo");

        RuleFor(x => x.Accion)
            .MaximumLength(50).When(x => !string.IsNullOrEmpty(x.Accion))
            .WithMessage("Acción demasiado larga");

        RuleFor(x => x.FechaFin)
            .GreaterThan(x => x.FechaInicio).When(x => x.FechaFin.HasValue && x.FechaInicio.HasValue)
            .WithMessage("Fecha fin debe ser posterior a fecha inicio");

        RuleFor(x => x.Observaciones)
            .MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Observaciones))
            .WithMessage("Observaciones demasiado largas");
    }
}