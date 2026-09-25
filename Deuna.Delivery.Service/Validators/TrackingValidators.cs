using Deuna.Delivery.Service.DTOs;
using FluentValidation;

namespace Deuna.Delivery.Service.Validators;

public class ActualizarUbicacionRequestValidator : AbstractValidator<ActualizarUbicacionRequest>
{
    public ActualizarUbicacionRequestValidator()
    {
        RuleFor(x => x.RiderId)
            .NotEmpty().WithMessage("El riderId es obligatorio");

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90).WithMessage("La latitud debe estar entre -90 y 90");

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180).WithMessage("La longitud debe estar entre -180 y 180");

        RuleFor(x => x.Timestamp)
            .NotEmpty().WithMessage("El timestamp es obligatorio");
    }
}

public class RechazarPedidoRequestValidator : AbstractValidator<RechazarPedidoRequest>
{
    public RechazarPedidoRequestValidator()
    {
        RuleFor(x => x.Motivo)
            .MaximumLength(300).WithMessage("El motivo no puede superar los 300 caracteres");
    }
}

public class ValidarQrLocalRequestValidator : AbstractValidator<ValidarQrLocalRequest>
{
    public ValidarQrLocalRequestValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("El pedido es obligatorio");

        RuleFor(x => x.TokenQrLocal)
            .NotEmpty().WithMessage("El token del QR es obligatorio")
            .MaximumLength(64).WithMessage("El token del QR es demasiado largo");
    }
}

public class AsignarPedidoRequestValidator : AbstractValidator<AsignarPedidoRequest>
{
    public AsignarPedidoRequestValidator()
    {
        // Solo que venga un repartidor. Que ese repartidor sirva para ese pedido no se
        // valida acá: depende del radio y de si está libre, y eso lo sabe el servicio, no
        // el formulario.
        RuleFor(x => x.RepartidorId)
            .NotEmpty().WithMessage("El repartidor es obligatorio");
    }
}

public class ValidarQrEntregaRequestValidator : AbstractValidator<ValidarQrEntregaRequest>
{
    public ValidarQrEntregaRequestValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("El pedido es obligatorio");

        RuleFor(x => x.TokenQrEntrega)
            .NotEmpty().WithMessage("El token del QR es obligatorio")
            .MaximumLength(64).WithMessage("El token del QR es demasiado largo");
    }
}
