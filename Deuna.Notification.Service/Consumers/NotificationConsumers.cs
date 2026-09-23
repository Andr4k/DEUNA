using Deuna.Notification.Service.Services;
using Deuna.Shared.Events;
using MassTransit;

namespace Deuna.Notification.Service.Consumers;

/// <summary>
/// El pedido se asignó automáticamente a un domiciliario (TASK-303): se le avisa para que
/// se dirija al restaurante.
///
/// La app igual consulta GET /my-orders, pero el push es lo que hace que se entere sin
/// abrir la app. El cuerpo lleva el código y los datos llevan el pedidoId para que la
/// notificación abra el pedido correcto.
/// </summary>
public class PedidoAsignadoConsumer : IConsumer<PedidoAsignado>
{
    private readonly INotificacionService _notificaciones;
    private readonly ILogger<PedidoAsignadoConsumer> _logger;

    public PedidoAsignadoConsumer(INotificacionService notificaciones, ILogger<PedidoAsignadoConsumer> logger)
    {
        _notificaciones = notificaciones;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PedidoAsignado> context)
    {
        var evento = context.Message;

        var mensaje = new MensajePush(
            Titulo: "Pedido asignado",
            Cuerpo: $"Tienes el pedido {evento.Codigo}. Dirígete al restaurante para recogerlo.",
            Datos: new Dictionary<string, string>
            {
                ["tipo"] = nameof(PedidoAsignado),
                ["pedidoId"] = evento.PedidoId.ToString(),
                ["codigo"] = evento.Codigo
            });

        await _notificaciones.NotificarAsync(
            evento.RepartidorId,
            nameof(PedidoAsignado),
            mensaje,
            context.MessageId,
            context.CancellationToken);

        _logger.LogInformation("Notificación de asignación procesada para el pedido {Codigo}", evento.Codigo);
    }
}

/// <summary>
/// Entró un pedido nuevo al sistema (TASK-202): se le avisa al restaurante para que lo
/// prepare.
/// </summary>
public class PedidoCreadoConsumer : IConsumer<PedidoCreado>
{
    private readonly INotificacionService _notificaciones;
    private readonly ILogger<PedidoCreadoConsumer> _logger;

    public PedidoCreadoConsumer(INotificacionService notificaciones, ILogger<PedidoCreadoConsumer> logger)
    {
        _notificaciones = notificaciones;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PedidoCreado> context)
    {
        var evento = context.Message;

        var mensaje = new MensajePush(
            Titulo: "Nuevo pedido",
            Cuerpo: $"Entró el pedido {evento.Codigo} por {evento.Total:N0}.",
            Datos: new Dictionary<string, string>
            {
                ["tipo"] = nameof(PedidoCreado),
                ["pedidoId"] = evento.PedidoId.ToString(),
                ["codigo"] = evento.Codigo
            });

        await _notificaciones.NotificarAsync(
            evento.RestauranteId,
            nameof(PedidoCreado),
            mensaje,
            context.MessageId,
            context.CancellationToken);

        _logger.LogInformation("Notificación de pedido nuevo procesada para {Codigo}", evento.Codigo);
    }
}
