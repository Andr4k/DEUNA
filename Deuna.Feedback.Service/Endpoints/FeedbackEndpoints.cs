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

        // POST /api/v1/feedback/submit-detailed — Nivel 2 (US-004.2)
        grupo.MapPost("/submit-detailed", async (
            SubmitDetailedFeedbackRequest request,
            IFeedbackService feedbackService,
            IValidator<SubmitDetailedFeedbackRequest> validator,
            CancellationToken cancellationToken) =>
        {
            var validacion = await validator.ValidateAsync(request, cancellationToken);
            if (!validacion.IsValid)
            {
                return Results.ValidationProblem(validacion.ToDictionary());
            }

            var resultado = await feedbackService.EnviarFeedbackDetalladoAsync(request, cancellationToken);

            return resultado.Resultado switch
            {
                ResultadoFeedback.Ok => Results.Ok(new SubmitDetailedFeedbackResponse(
                    EncuestaId: resultado.Encuesta!.Id,
                    Mensaje: "¡Gracias por el detalle!",
                    PuedeCompartirWhatsApp: resultado.PuedeCompartirWhatsApp)),

                ResultadoFeedback.PedidoNoEncontrado => Results.NotFound(new
                {
                    message = $"El pedido {request.PedidoId} no existe"
                }),

                // 400 y no 404: el pedido existe, lo que falta es el paso previo.
                ResultadoFeedback.Nivel1Requerido => Results.BadRequest(new
                {
                    message = "El Nivel 2 solo se puede enviar después del Nivel 1"
                }),

                ResultadoFeedback.DetalleDuplicado => Results.BadRequest(new
                {
                    message = "El pedido ya tiene feedback detallado"
                }),

                // El Nivel 2 es para quien quiere recomendar el restaurante (US-004.2).
                ResultadoFeedback.RatingBajoParaNivel2 => Results.BadRequest(new
                {
                    message = "El Nivel 2 solo se habilita con 4 o más estrellas de comida"
                }),

                _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
            };
        })
        .AllowAnonymous()
        .WithName("SubmitDetailedFeedback")
        .WithSummary("Registra el feedback Nivel 2 (criterios detallados, comentario y foto opcionales)");

        // POST /api/v1/feedback/share-whatsapp — viralidad (US-004.3)
        grupo.MapPost("/share-whatsapp", async (
            ShareWhatsAppRequest request,
            IFeedbackService feedbackService,
            IValidator<ShareWhatsAppRequest> validator,
            CancellationToken cancellationToken) =>
        {
            var validacion = await validator.ValidateAsync(request, cancellationToken);
            if (!validacion.IsValid)
            {
                return Results.ValidationProblem(validacion.ToDictionary());
            }

            var resultado = await feedbackService.RegistrarCompartidoWhatsAppAsync(request, cancellationToken);

            return resultado.Resultado switch
            {
                ResultadoFeedback.Ok => Results.Ok(new ShareWhatsAppResponse(
                    Enlace: resultado.Enlace!,
                    Mensaje: resultado.Mensaje!,
                    YaCompartido: resultado.YaCompartido)),

                ResultadoFeedback.PedidoNoEncontrado => Results.NotFound(new
                {
                    message = $"El pedido {request.PedidoId} no tiene feedback registrado"
                }),

                // FR-004.5: con menos de 4 estrellas el botón no aparece en la PWA, así que
                // el endpoint tampoco acepta el compartido.
                ResultadoFeedback.RatingBajoParaCompartir => Results.BadRequest(new
                {
                    message = "Solo se puede recomendar un pedido con 4 o más estrellas de comida"
                }),

                _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
            };
        })
        .AllowAnonymous()
        .WithName("ShareWhatsApp")
        .WithSummary("Registra el compartido por WhatsApp y devuelve el enlace corto");

        return app;
    }
}
