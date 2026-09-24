using System.Security.Cryptography;
using Deuna.Feedback.Service.DTOs;
using Deuna.Feedback.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Deuna.Feedback.Service.Services;

/// <summary>
/// Registro del feedback en sus tres niveles (SPEC-004).
///
/// Valida contra la proyección local de pedidos (sin acoplamiento síncrono con Orders):
/// el pedido debe existir, estar <c>Entregado</c> y no tener feedback previo.
/// </summary>
public class FeedbackService : IFeedbackService
{
    /// <summary>
    /// Alfabeto del código corto. Sin I, O, 0 ni 1: el código viaja dentro de un enlace que
    /// la gente reenvía a mano, y esos caracteres se confunden al leerlo.
    /// </summary>
    private const string AlfabetoCodigo = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private const int LargoCodigo = 8;

    private readonly FeedbackDbContext _db;
    private readonly FeedbackOptions _options;
    private readonly ILogger<FeedbackService> _logger;

    public FeedbackService(FeedbackDbContext db, IOptions<FeedbackOptions> options, ILogger<FeedbackService> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ResultadoEnvioFeedback> EnviarFeedbackBasicoAsync(
        SubmitBasicFeedbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var pedido = await _db.PedidosReplicados
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PedidoId == request.PedidoId, cancellationToken);

        if (pedido is null)
        {
            _logger.LogWarning("Feedback rechazado: el pedido {PedidoId} no existe en la proyección", request.PedidoId);
            return new ResultadoEnvioFeedback(ResultadoFeedback.PedidoNoEncontrado);
        }

        if (!string.Equals(pedido.Estado, PedidoReplicado.EstadoEntregado, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Feedback rechazado: el pedido {PedidoId} está en estado {Estado} y no Entregado",
                request.PedidoId, pedido.Estado);
            return new ResultadoEnvioFeedback(ResultadoFeedback.PedidoNoEntregado);
        }

        var yaTieneFeedback = await _db.FeedbackEncuestas
            .AnyAsync(e => e.PedidoId == request.PedidoId, cancellationToken);

        if (yaTieneFeedback)
        {
            _logger.LogWarning("Feedback rechazado: el pedido {PedidoId} ya tiene feedback registrado", request.PedidoId);
            return new ResultadoEnvioFeedback(ResultadoFeedback.FeedbackDuplicado);
        }

        var encuesta = new FeedbackEncuesta
        {
            PedidoId = pedido.PedidoId,
            RestauranteId = pedido.RestauranteId,
            RepartidorId = pedido.RepartidorId,
            RatingGeneralComida = request.RatingGeneralComida,
            RatingServicioRepartidor = request.RatingServicioRepartidor,
            DeseaRecomendar = request.DeseaRecomendar
        };

        _db.FeedbackEncuestas.Add(encuesta);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // El índice único sobre PedidoId cubre la carrera de dos envíos simultáneos:
            // el segundo se traduce en "ya tiene feedback" en lugar de un 500.
            _logger.LogWarning(ex, "Feedback duplicado detectado al guardar para el pedido {PedidoId}", request.PedidoId);
            return new ResultadoEnvioFeedback(ResultadoFeedback.FeedbackDuplicado);
        }

        _logger.LogInformation(
            "Feedback Nivel 1 registrado: EncuestaId={EncuestaId} PedidoId={PedidoId} Comida={Comida} Repartidor={Repartidor}",
            encuesta.Id, encuesta.PedidoId, encuesta.RatingGeneralComida, encuesta.RatingServicioRepartidor);

        return new ResultadoEnvioFeedback(ResultadoFeedback.Ok, encuesta);
    }

    public async Task<ResultadoEnvioDetallado> EnviarFeedbackDetalladoAsync(
        SubmitDetailedFeedbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var encuesta = await _db.FeedbackEncuestas
            .FirstOrDefaultAsync(e => e.PedidoId == request.PedidoId, cancellationToken);

        if (encuesta is null)
        {
            // El Nivel 2 cuelga de la encuesta del Nivel 1: sin encuesta no hay dónde
            // guardarlo. Se distingue "falta el Nivel 1" de "el pedido no existe" porque
            // el cliente tiene que poder entender qué le falta hacer.
            var pedidoExiste = await _db.PedidosReplicados
                .AnyAsync(p => p.PedidoId == request.PedidoId, cancellationToken);

            _logger.LogWarning(
                "Nivel 2 rechazado para {PedidoId}: {Motivo}",
                request.PedidoId, pedidoExiste ? "falta el Nivel 1" : "el pedido no existe");

            return new ResultadoEnvioDetallado(
                pedidoExiste ? ResultadoFeedback.Nivel1Requerido : ResultadoFeedback.PedidoNoEncontrado);
        }

        // El Nivel 2 es para quien quiere recomendar el restaurante (US-004.2): por debajo del
        // rating mínimo la PWA ni lo ofrece (mostrarNivel2 del Nivel 1), así que el endpoint
        // tampoco lo acepta.
        if (encuesta.RatingGeneralComida < _options.RatingMinimoParaCompartir)
        {
            _logger.LogWarning(
                "Nivel 2 rechazado: el pedido {PedidoId} calificó la comida con {Rating}",
                request.PedidoId, encuesta.RatingGeneralComida);

            return new ResultadoEnvioDetallado(ResultadoFeedback.RatingBajoParaNivel2);
        }

        var yaTieneDetalle = await _db.FeedbackDetallesCriterios.AnyAsync(c => c.EncuestaId == encuesta.Id, cancellationToken)
            || await _db.FeedbackComentariosFotos.AnyAsync(c => c.EncuestaId == encuesta.Id, cancellationToken);

        if (yaTieneDetalle)
        {
            _logger.LogWarning("Nivel 2 rechazado: el pedido {PedidoId} ya tiene detalle", request.PedidoId);
            return new ResultadoEnvioDetallado(ResultadoFeedback.DetalleDuplicado);
        }

        _db.FeedbackDetallesCriterios.AddRange(request.Criterios.Select(criterio => new FeedbackDetalleCriterio
        {
            EncuestaId = encuesta.Id,
            Criterio = (criterio.Criterio ?? string.Empty).Trim(),
            Puntaje = criterio.Puntaje
        }));

        var comentario = string.IsNullOrWhiteSpace(request.Comentario) ? null : request.Comentario.Trim();
        var fotoUrl = string.IsNullOrWhiteSpace(request.FotoUrl) ? null : request.FotoUrl.Trim();

        if (comentario is not null || fotoUrl is not null)
        {
            _db.FeedbackComentariosFotos.Add(new FeedbackComentarioFoto
            {
                EncuestaId = encuesta.Id,
                ComentarioTexto = comentario,
                UrlFotoEvidencia = fotoUrl
            });
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // El índice único (EncuestaId, Criterio) cubre la carrera de dos envíos
            // simultáneos: el segundo se traduce en "ya tiene detalle", no en un 500.
            _logger.LogWarning(ex, "Nivel 2 duplicado detectado al guardar para el pedido {PedidoId}", request.PedidoId);
            return new ResultadoEnvioDetallado(ResultadoFeedback.DetalleDuplicado);
        }

        _logger.LogInformation(
            "Feedback Nivel 2 registrado: EncuestaId={EncuestaId} PedidoId={PedidoId} Criterios={Criterios} Comentario={ConComentario} Foto={ConFoto}",
            encuesta.Id, encuesta.PedidoId, request.Criterios.Count, comentario is not null, fotoUrl is not null);

        return new ResultadoEnvioDetallado(
            ResultadoFeedback.Ok,
            encuesta,
            PuedeCompartirWhatsApp: encuesta.RatingGeneralComida >= _options.RatingMinimoParaCompartir);
    }

    public async Task<ResultadoCompartir> RegistrarCompartidoWhatsAppAsync(
        ShareWhatsAppRequest request,
        CancellationToken cancellationToken = default)
    {
        var encuesta = await _db.FeedbackEncuestas
            .FirstOrDefaultAsync(e => e.PedidoId == request.PedidoId, cancellationToken);

        if (encuesta is null)
        {
            _logger.LogWarning("Compartido rechazado: el pedido {PedidoId} no tiene feedback Nivel 1", request.PedidoId);
            return new ResultadoCompartir(ResultadoFeedback.PedidoNoEncontrado);
        }

        // FR-004.5: con menos de 4 estrellas el botón ni siquiera aparece, así que el
        // endpoint tampoco puede aceptar el compartido.
        if (encuesta.RatingGeneralComida < _options.RatingMinimoParaCompartir)
        {
            _logger.LogWarning(
                "Compartido rechazado: el pedido {PedidoId} calificó la comida con {Rating}",
                request.PedidoId, encuesta.RatingGeneralComida);

            return new ResultadoCompartir(ResultadoFeedback.RatingBajoParaCompartir);
        }

        var existente = await _db.CompartidosWhatsApp
            .FirstOrDefaultAsync(c => c.EncuestaId == encuesta.Id, cancellationToken);

        if (existente is not null)
        {
            // Idempotente: el cliente pudo tocar el botón dos veces o haber perdido el
            // mensaje. Se le devuelve el mismo enlace que ya mandó.
            var nombreExistente = await ObtenerNombreRestauranteAsync(encuesta.RestauranteId, cancellationToken);
            var enlaceExistente = ConstruirEnlace(existente.CodigoCompartido);

            _logger.LogInformation("Compartido repetido para el pedido {PedidoId}: se devuelve el enlace existente", request.PedidoId);

            return new ResultadoCompartir(
                ResultadoFeedback.Ok,
                enlaceExistente,
                ConstruirMensaje(nombreExistente, enlaceExistente),
                YaCompartido: true);
        }

        var codigo = await GenerarCodigoUnicoAsync(cancellationToken);

        _db.CompartidosWhatsApp.Add(new FeedbackViralWhatsApp
        {
            EncuestaId = encuesta.Id,
            FueCompartido = true,
            FechaCompartido = DateTime.UtcNow,
            CodigoCompartido = codigo
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Carrera: otro request registró el compartido primero. Se devuelve el que quedó.
            _logger.LogWarning(ex, "Compartido concurrente para el pedido {PedidoId}", request.PedidoId);

            var ganador = await _db.CompartidosWhatsApp
                .FirstOrDefaultAsync(c => c.EncuestaId == encuesta.Id, cancellationToken);

            if (ganador is null)
            {
                return new ResultadoCompartir(ResultadoFeedback.PedidoNoEncontrado);
            }

            codigo = ganador.CodigoCompartido;
        }

        var nombre = await ObtenerNombreRestauranteAsync(encuesta.RestauranteId, cancellationToken);
        var enlace = ConstruirEnlace(codigo);

        _logger.LogInformation(
            "Compartido por WhatsApp registrado: PedidoId={PedidoId} Codigo={Codigo}",
            request.PedidoId, codigo);

        return new ResultadoCompartir(ResultadoFeedback.Ok, enlace, ConstruirMensaje(nombre, enlace));
    }

    /// <summary>
    /// Nombre del restaurante para el mensaje viral (US-004.3). Si la réplica todavía no
    /// llegó se usa un nombre genérico: el compartido no puede fallar por eso.
    /// </summary>
    private async Task<string> ObtenerNombreRestauranteAsync(Guid restauranteId, CancellationToken cancellationToken)
    {
        var nombre = await _db.RestaurantesReplicados
            .Where(r => r.Id == restauranteId)
            .Select(r => r.NombreComercial)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(nombre))
        {
            return nombre;
        }

        _logger.LogWarning(
            "El restaurante {RestauranteId} no está en la réplica: el mensaje usará el nombre por defecto",
            restauranteId);

        return _options.NombreRestaurantePorDefecto;
    }

    /// <summary>
    /// Código corto único para el enlace compartido. Aleatorio criptográfico: un código
    /// predecible dejaría adivinar los compartidos de otros clientes.
    /// </summary>
    private async Task<string> GenerarCodigoUnicoAsync(CancellationToken cancellationToken)
    {
        for (var intento = 0; intento < 5; intento++)
        {
            var codigo = string.Create(LargoCodigo, AlfabetoCodigo, (destino, alfabeto) =>
            {
                for (var i = 0; i < destino.Length; i++)
                {
                    destino[i] = alfabeto[RandomNumberGenerator.GetInt32(alfabeto.Length)];
                }
            });

            var ocupado = await _db.CompartidosWhatsApp
                .AnyAsync(c => c.CodigoCompartido == codigo, cancellationToken);

            if (!ocupado)
            {
                return codigo;
            }
        }

        // Con 32^8 combinaciones, cinco colisiones seguidas no son azar.
        throw new InvalidOperationException("No se pudo generar un código de compartido único");
    }

    private string ConstruirEnlace(string codigo)
        => $"{_options.UrlBaseCompartir.TrimEnd('/')}/{codigo}";

    private string ConstruirMensaje(string nombreRestaurante, string enlace)
        => string.Format(_options.PlantillaMensajeCompartir, nombreRestaurante, enlace);
}
