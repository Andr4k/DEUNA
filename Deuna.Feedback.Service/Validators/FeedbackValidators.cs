using Deuna.Feedback.Service.DTOs;
using Deuna.Feedback.Service.Models;
using FluentValidation;

namespace Deuna.Feedback.Service.Validators;

public class SubmitBasicFeedbackRequestValidator : AbstractValidator<SubmitBasicFeedbackRequest>
{
    public SubmitBasicFeedbackRequestValidator()
    {
        RuleFor(x => x.PedidoId)
            .NotEmpty().WithMessage("El pedidoId es obligatorio");

        RuleFor(x => x.RatingGeneralComida)
            .InclusiveBetween(1, 5).WithMessage("La calificación de la comida debe estar entre 1 y 5");

        RuleFor(x => x.RatingServicioRepartidor)
            .InclusiveBetween(1, 5).WithMessage("La calificación del repartidor debe estar entre 1 y 5");
    }
}

public class SubmitDetailedFeedbackRequestValidator : AbstractValidator<SubmitDetailedFeedbackRequest>
{
    public SubmitDetailedFeedbackRequestValidator()
    {
        RuleFor(x => x.PedidoId)
            .NotEmpty().WithMessage("El pedidoId es obligatorio");

        RuleFor(x => x.Criterios)
            .NotEmpty().WithMessage("El Nivel 2 requiere al menos un criterio evaluado");

        RuleForEach(x => x.Criterios).ChildRules(criterio =>
        {
            criterio.RuleFor(c => c.Criterio)
                .NotEmpty().WithMessage("El nombre del criterio es obligatorio")
                .MaximumLength(50).WithMessage("El criterio no puede superar los 50 caracteres");

            criterio.RuleFor(c => c.Puntaje)
                .InclusiveBetween(1, 5).WithMessage("El puntaje debe estar entre 1 y 5");
        });

        // Dos puntajes para el mismo criterio serían un dato ambiguo.
        RuleFor(x => x.Criterios)
            .Must(criterios => criterios
                .Select(c => (c.Criterio ?? string.Empty).Trim())
                .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
                .All(grupo => grupo.Count() == 1))
            .WithMessage("Un criterio no puede evaluarse dos veces en el mismo envío")
            .When(x => x.Criterios is { Count: > 0 });

        RuleFor(x => x.Comentario)
            .MaximumLength(1000).WithMessage("El comentario no puede superar los 1000 caracteres");

        RuleFor(x => x.FotoUrl)
            .MaximumLength(500).WithMessage("La URL de la foto no puede superar los 500 caracteres");
    }
}

public class ShareWhatsAppRequestValidator : AbstractValidator<ShareWhatsAppRequest>
{
    public ShareWhatsAppRequestValidator()
    {
        RuleFor(x => x.PedidoId)
            .NotEmpty().WithMessage("El pedidoId es obligatorio");
    }
}
