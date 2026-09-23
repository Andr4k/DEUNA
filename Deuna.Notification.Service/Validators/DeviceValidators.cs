using Deuna.Notification.Service.DTOs;
using FluentValidation;

namespace Deuna.Notification.Service.Validators;

public class RegistrarDispositivoRequestValidator : AbstractValidator<RegistrarDispositivoRequest>
{
    private static readonly string[] Plataformas = ["android", "ios", "web"];

    public RegistrarDispositivoRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("El token del dispositivo es obligatorio")
            .MaximumLength(500).WithMessage("El token del dispositivo es demasiado largo");

        RuleFor(x => x.Plataforma)
            .NotEmpty().WithMessage("La plataforma es obligatoria")
            .Must(p => Plataformas.Contains(p, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Plataforma inválida: se espera android, ios o web");
    }
}

public class DesregistrarDispositivoRequestValidator : AbstractValidator<DesregistrarDispositivoRequest>
{
    public DesregistrarDispositivoRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("El token del dispositivo es obligatorio")
            .MaximumLength(500).WithMessage("El token del dispositivo es demasiado largo");
    }
}
