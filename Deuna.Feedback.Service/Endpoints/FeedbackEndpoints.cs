using Deuna.Feedback.Service.DTOs;
using Deuna.Feedback.Service.Services;
using FluentValidation;

namespace Deuna.Feedback.Service.Endpoints;

/// <summary>
/// Endpoints de feedback (plan técnico: /api/v1/feedback).
/// </summary>
public static class FeedbackEndpoints
{
    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/v1/feedback")
            .WithTags("Feedback");

        // POST /api/v1/feedback/submit-basic — Nivel 1 (US-004.1)
        grupo.MapPost("/submit-basic", async (
            SubmitBasicFeedbackRequest request,
            IFeedbackService feedbackService,
            IValidator<SubmitBasicFeedbackRequest> validator,
            CancellationToken cancellationToken) =>
        {
            var validacion = await validator.ValidateAsync(request, cancellationToken);
            if (!validacion.IsValid)
            {
                return Results.ValidationProblem(validacion.ToDictionary());
            }

            var resultado = await feedbackService.EnviarFeedbackBasicoAsync(request, cancellationToken);

            return resultado.Resultado switch
            {
                ResultadoFeedback.Ok => Results.Ok(new SubmitBasicFeedbackResponse(
                    EncuestaId: resultado.Encuesta!.Id,
                    Mensaje: "¡Gracias por tu feedback!",
                    MostrarNivel2: resultado.Encuesta.RatingGeneralComida >= 4)),

                ResultadoFeedback.PedidoNoEncontrado => Results.NotFound(new
                {
                    message = $"El pedido {request.PedidoId} no existe"
                }),

                ResultadoFeedback.PedidoNoEntregado => Results.BadRequest(new
                {
                    message = "Solo se puede enviar feedback de pedidos entregados"
                }),

                ResultadoFeedback.FeedbackDuplicado => Results.BadRequest(new
                {
                    message = "El pedido ya tiene feedback registrado"
                }),

                _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
            };
        })
        // El plan técnico lo define como público (token del pedido): el cliente llega
        // desde la PWA tras escanear el QR, sin sesión.
        .AllowAnonymous()
        .WithName("SubmitBasicFeedback")
        .WithSummary("Registra el feedback Nivel 1 de un pedido entregado");

        return app;
    }
}
