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
