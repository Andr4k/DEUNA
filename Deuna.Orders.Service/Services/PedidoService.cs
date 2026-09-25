using System.Security.Cryptography;
using Deuna.Orders.Service.Consumers;
using Deuna.Orders.Service.Models;
using Deuna.Orders.Service.DTOs;
using Deuna.Shared.Domain;
using Deuna.Shared.Events;
using Deuna.Shared.Security;
using Deuna.Shared.Extensions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;

namespace Deuna.Orders.Service.Services;

/// <summary>
/// Servicio principal para gestión de pedidos
/// </summary>
public interface IPedidoService
{
    Task<CrearPedidoResponse> CrearPedidoAsync(CrearPedidoRequest request, HttpContext httpContext);
    Task<PedidoResponse?> ObtenerPedidoAsync(Guid pedidoId);

    /// <summary>
    /// Cancela un pedido. Solo el restaurante dueño (o un administrador), y solo mientras el
    /// pedido no esté confirmado en el local: después, la comida ya está en juego.
    /// Devuelve null si el pedido no existe.
    /// </summary>
    Task<PedidoResponse?> CancelarPedidoAsync(Guid pedidoId, string motivo, HttpContext httpContext);
    Task<List<PedidoResponse>> ObtenerPedidosPorClienteAsync(Guid clienteId);
    Task<List<PedidoResponse>> ObtenerPedidosPorRestauranteAsync(Guid restauranteId);

    /// <summary>
    /// Listado paginado de pedidos sin asignar para el panel del administrador.
    /// De solo lectura: no cambia el estado de ningún pedido.
    /// </summary>
    Task<ListaPedidosSinAsignarResponse> ObtenerPedidosSinAsignarAsync(FiltroPedidosSinAsignar filtro);

    /// <summary>
    /// Historial de servicios finalizados para el panel del administrador: las filas
    /// paginadas y los KPIs de la cabecera, calculados sobre el MISMO conjunto filtrado.
    /// De solo lectura: no cambia el estado de ningún pedido.
    /// </summary>
    Task<RespuestaServiciosFinalizados> ObtenerServiciosFinalizadosAsync(FiltroServiciosFinalizados filtro);
}

public class PedidoService : IPedidoService
{
    /// <summary>Tipo del contacto que representa al cliente en <c>contactos_pedido</c>.</summary>
    private const string TipoContactoCliente = "Cliente";

    private readonly OrdersDbContext _db;
    private readonly ITarifaService _tarifaService;
    private readonly IGeoService _geoService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IOptions<OrdersOptions> _ordersOptions;
    private readonly ILogger<PedidoService> _logger;

    public PedidoService(
        OrdersDbContext db,
        ITarifaService tarifaService,
        IGeoService geoService,
        IPublishEndpoint publishEndpoint,
        IOptions<OrdersOptions> ordersOptions,
        ILogger<PedidoService> logger)
    {
        _db = db;
        _tarifaService = tarifaService;
        _geoService = geoService;
        _publishEndpoint = publishEndpoint;
        _ordersOptions = ordersOptions;
        _logger = logger;
    }

    public async Task<CrearPedidoResponse> CrearPedidoAsync(CrearPedidoRequest request, HttpContext httpContext)
    {
        // Obtener ClienteId desde el JWT (rol RESTAURANT)
        var clienteId = httpContext.GetUserId() ?? throw new UnauthorizedAccessException("Usuario no autenticado");
        var rol = httpContext.GetRole();

        if (rol != Roles.Restaurant)
        {
            throw new UnauthorizedAccessException("Solo los restaurantes pueden crear pedidos");
        }

        // Aislamiento entre restaurantes: el alta solo puede operar sobre el restaurante
        // del token, con la misma regla que ya aplica la consulta por restaurante.
        // Sin esto, cualquier restaurante podía crear pedidos a nombre de otro.
        if (!string.Equals(rol, Roles.Admin, StringComparison.OrdinalIgnoreCase) && clienteId != request.RestauranteId)
        {
            throw new UnauthorizedAccessException("Un restaurante solo puede crear pedidos para sí mismo");
        }

        // Verificar que el restaurante existe y está activo
        // Include obligatorio: EstaEnHorarioAtencion lee restaurante.HorariosAtencion;
        // sin la navegación cargada la colección llega vacía y todo pedido se rechaza con 400.
        var restaurante = await _db.RestaurantesReplicados
            .Include(r => r.HorariosAtencion)
            .FirstOrDefaultAsync(r => r.Id == request.RestauranteId && r.Activo && r.AceptaPedidos);

        if (restaurante == null)
        {
            throw new InvalidOperationException($"Restaurante {request.RestauranteId} no encontrado, inactivo o no acepta pedidos");
        }

        // Verificar horario de atención
        if (!EstaEnHorarioAtencion(restaurante))
        {
            throw new InvalidOperationException("El restaurante está fuera de su horario de atención");
        }

        // Crear punto de geografía para la dirección de entrega
        var puntoEntrega = new Point(
            (double)request.DireccionEntrega.Longitud,
            (double)request.DireccionEntrega.Latitud) { SRID = 4326 };

        // Calcular tarifa
        var itemsTemp = request.Items.Select(i => new ItemPedido
        {
            NombreProducto = i.NombreProducto,
            Descripcion = i.Descripcion,
            Cantidad = i.Cantidad,
            PrecioUnitario = i.PrecioUnitario,
            Subtotal = i.Cantidad * i.PrecioUnitario
        }).ToList();

        var subtotal = itemsTemp.Sum(i => i.Subtotal);
        var tarifa = await _tarifaService.CalcularTarifaAsync(
            request.RestauranteId,
            (double)request.DireccionEntrega.Latitud,
            (double)request.DireccionEntrega.Longitud,
            subtotal,
            itemsTemp);

        // Crear dirección de entrega
        var direccionEntrega = new DireccionEntrega
        {
            Calle = request.DireccionEntrega.Calle,
            Numero = request.DireccionEntrega.Numero,
            Interior = request.DireccionEntrega.Interior,
            Referencia = request.DireccionEntrega.Referencia,
            Ciudad = request.DireccionEntrega.Ciudad,
            Departamento = request.DireccionEntrega.Departamento,
            CodigoPostal = request.DireccionEntrega.CodigoPostal,
            Ubicacion = puntoEntrega
        };

        _db.DireccionesEntrega.Add(direccionEntrega);
        await _db.SaveChangesAsync();

        // Generar código PED-XXXXXX
        var codigo = await GenerarCodigoPedidoAsync();

        // Dos tokens, no uno: el del local lo escanea el domiciliario al llegar y el de
        // entrega lo escanea el cliente al recibir (FR-002.5). Son de partes distintas, así
        // que no pueden compartir valor.
        var tokenQrLocal = GenerarTokenQr();
        var tokenQrEntrega = GenerarTokenQr();

        // Crear pedido principal
        var pedido = new Pedido
        {
            ClienteId = clienteId,
            RestauranteId = request.RestauranteId,
            Codigo = codigo,
            Estado = EstadosPedido.Buscando,
            Subtotal = subtotal,
            CostoEnvio = tarifa.TotalCalculado,
            Total = subtotal + tarifa.TotalCalculado,
            DireccionEntregaId = direccionEntrega.Id,
            TokenQrLocal = tokenQrLocal,
            TokenQrEntrega = tokenQrEntrega,
            NotasCliente = request.NotasCliente
        };

        _db.Pedidos.Add(pedido);
        await _db.SaveChangesAsync();

        // Crear items del pedido
        var items = request.Items.Select((item, index) => new ItemPedido
        {
            PedidoId = pedido.Id,
            NombreProducto = item.NombreProducto,
            Descripcion = item.Descripcion,
            Cantidad = item.Cantidad,
            PrecioUnitario = item.PrecioUnitario,
            Subtotal = item.Cantidad * item.PrecioUnitario
        }).ToList();

        _db.ItemsPedido.AddRange(items);

        // Crear contactos
        var contactos = request.Contactos.Select(c => new ContactoPedido
        {
            PedidoId = pedido.Id,
            Tipo = c.Tipo,
            Nombre = c.Nombre,
            Telefono = c.Telefono,
            Email = c.Email
        }).ToList();

        _db.ContactosPedido.AddRange(contactos);

        // Crear tarifa aplicada (snapshot inmutable)
        var tarifaAplicada = new TarifaAplicada
        {
            PedidoId = pedido.Id,
            TipoTarifa = tarifa.TipoTarifa,
            CostoBase = tarifa.CostoBase,
            CostoPorKm = tarifa.CostoPorKm,
            DistanciaKm = tarifa.DistanciaKm,
            CostoAdicional = tarifa.CostoAdicional,
            DetalleCalculo = tarifa.DetalleCalculo,
            TotalCalculado = tarifa.TotalCalculado
        };

        _db.TarifasAplicadas.Add(tarifaAplicada);

        await _db.SaveChangesAsync();

        // Publicar evento PedidoCreado
        var evento = new PedidoCreado(
            PedidoId: pedido.Id,
            Codigo: pedido.Codigo,
            ClienteId: pedido.ClienteId,
            RestauranteId: pedido.RestauranteId,
            Total: pedido.Total,
            Estado: pedido.Estado,
            TokenQrLocal: pedido.TokenQrLocal,
            TokenQrEntrega: pedido.TokenQrEntrega,
            OccurredAt: DateTime.UtcNow,
            // Punto de entrega para el tracking geoespacial de Delivery
            Latitud: (double)request.DireccionEntrega.Latitud,
            Longitud: (double)request.DireccionEntrega.Longitud
        );

        await _publishEndpoint.Publish(evento);

        _logger.LogInformation("Pedido creado: {Codigo} (ID: {PedidoId}) para restaurante {RestauranteId}",
            pedido.Codigo, pedido.Id, pedido.RestauranteId);

        return new CrearPedidoResponse(
            PedidoId: pedido.Id,
            Codigo: pedido.Codigo,
            TokenQrLocal: pedido.TokenQrLocal,
            Estado: pedido.Estado,
            Subtotal: pedido.Subtotal,
            CostoEnvio: pedido.CostoEnvio,
            Total: pedido.Total,
            FechaCreacion: pedido.FechaCreacion
        );
    }

    public async Task<PedidoResponse?> ObtenerPedidoAsync(Guid pedidoId)
    {
        var pedido = await _db.Pedidos
            .Include(p => p.DireccionEntrega)
            .Include(p => p.Restaurante)
            .Include(p => p.Items)
            .Include(p => p.Contactos)
            .Include(p => p.Tarifa)
            .FirstOrDefaultAsync(p => p.Id == pedidoId);

        if (pedido == null) return null;

        return MapToResponse(pedido);
    }

    /// <summary>
    /// El pedido con todo lo que <see cref="MapToResponse"/> necesita.
    ///
    /// Las navegaciones se cargan solo si se piden: sin estos Include, mapear revienta con
    /// un null. Vive acá para que un método nuevo no tenga que recordarlo.
    /// </summary>
    private Task<Pedido?> CargarPedidoCompletoAsync(Guid pedidoId) =>
        _db.Pedidos
            .Include(p => p.DireccionEntrega)
            .Include(p => p.Restaurante)
            .Include(p => p.Items)
            .Include(p => p.Contactos)
            .Include(p => p.Tarifa)
            .FirstOrDefaultAsync(p => p.Id == pedidoId);

    public async Task<PedidoResponse?> CancelarPedidoAsync(Guid pedidoId, string motivo, HttpContext httpContext)
    {
        var pedido = await CargarPedidoCompletoAsync(pedidoId);
        if (pedido is null)
        {
            return null;
        }

        // Mismo criterio que el resto del servicio: el restaurante dueño o un administrador.
        var userId = httpContext.GetUserId();
        var rol = httpContext.GetRole();

        if (userId != pedido.RestauranteId && rol != Roles.Admin)
        {
            throw new UnauthorizedAccessException("El pedido no pertenece a este restaurante");
        }

        // Se cancela mientras la comida no esté en juego. Después de confirmar en el local el
        // domiciliario ya la tiene: eso no se arregla cancelando, y aceptarlo dejaría un
        // pedido cancelado con la comida repartida.
        if (pedido.Estado != EstadosPedido.Buscando && pedido.Estado != EstadosPedido.Asignado)
        {
            throw new InvalidOperationException(
                $"El pedido {pedido.Codigo} no se puede cancelar en estado {pedido.Estado}: ya fue confirmado en el local");
        }

        pedido.Estado = EstadosPedido.Cancelado;
        pedido.FechaCancelacion = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // Este evento es lo único que hace que Delivery se entere. Sin él, allá el pedido
        // sigue contando como activo y el domiciliario queda ocupado para siempre.
        await _publishEndpoint.Publish(new PedidoCancelado(
            PedidoId: pedido.Id,
            Codigo: pedido.Codigo,
            Motivo: motivo,
            OccurredAt: pedido.FechaCancelacion.Value));

        _logger.LogInformation(
            "Pedido {Codigo} cancelado por el restaurante {RestauranteId}: {Motivo}",
            pedido.Codigo, pedido.RestauranteId, motivo);

        return MapToResponse(pedido);
    }

    public async Task<List<PedidoResponse>> ObtenerPedidosPorClienteAsync(Guid clienteId)
    {
        var pedidos = await _db.Pedidos
            .Include(p => p.DireccionEntrega)
            .Include(p => p.Restaurante)
            .Include(p => p.Items)
            .Include(p => p.Contactos)
            .Include(p => p.Tarifa)
            .Where(p => p.ClienteId == clienteId)
            .OrderByDescending(p => p.FechaCreacion)
            .ToListAsync();

        return pedidos.Select(MapToResponse).ToList();
    }

    public async Task<List<PedidoResponse>> ObtenerPedidosPorRestauranteAsync(Guid restauranteId)
    {
        var pedidos = await _db.Pedidos
            .Include(p => p.DireccionEntrega)
            .Include(p => p.Restaurante)
            .Include(p => p.Items)
            .Include(p => p.Contactos)
            .Include(p => p.Tarifa)
            .Where(p => p.RestauranteId == restauranteId)
            .OrderByDescending(p => p.FechaCreacion)
            .ToListAsync();

        return pedidos.Select(MapToResponse).ToList();
    }

    public async Task<ListaPedidosSinAsignarResponse> ObtenerPedidosSinAsignarAsync(FiltroPedidosSinAsignar filtro)
    {
        var ahora = DateTime.UtcNow;
        var opciones = _ordersOptions.Value;

        // El tipo se declara explícito a propósito: `var` sobre un `Include` infiere
        // IIncludableQueryable, y después no se le puede asignar el resultado de un
        // Where. La consulta se arma por partes y todas devuelven IQueryable.
        IQueryable<Pedido> query = _db.Pedidos.AsNoTracking()
            .Include(p => p.Restaurante)
            .Include(p => p.DireccionEntrega)
            .Include(p => p.Tarifa);

        // "Sin asignar" es el estado Buscando: el pedido al que el sistema no le encontró
        // candidato dentro del radio. Un Asignado solo entra si el umbral de "no responde"
        // está configurado (> 0): en 0 el filtro queda apagado y no se suma ninguno, porque
        // el criterio real todavía no está definido.
        if (filtro.IncluirSinRespuesta && opciones.MinutosSinRespuesta > 0)
        {
            var corte = ahora.AddMinutes(-opciones.MinutosSinRespuesta);

            // Un pedido puede pasar por varios domiciliarios (1:N): vale la ÚLTIMA
            // asignación. Si nunca se asignó, el MAX es NULL y la comparación no se cumple,
            // así que un Asignado sin historial queda fuera.
            query = query.Where(p =>
                p.Estado == EstadosPedido.Buscando ||
                (p.Estado == EstadosPedido.Asignado &&
                 _db.AsignacionesReplicadas
                     .Where(a => a.PedidoId == p.Id)
                     .Max(a => (DateTime?)a.FechaAsignacion) <= corte));
        }
        else
        {
            query = query.Where(p => p.Estado == EstadosPedido.Buscando);
        }

        // La zona es la ciudad del restaurante: no hay geometría de zona en Orders.
        if (!string.IsNullOrWhiteSpace(filtro.Zona))
        {
            var zona = filtro.Zona.Trim().ToLower();
            query = query.Where(p => p.Restaurante.Ciudad.ToLower() == zona);
        }

        // El valor del domicilio es la tarifa aplicada; si el pedido no tiene tarifa, cae
        // al costo de envío que quedó en el pedido.
        if (!string.IsNullOrWhiteSpace(filtro.Prioridad))
        {
            var prioridad = filtro.Prioridad.Trim();
            query = prioridad switch
            {
                OrdersOptions.PrioridadAlta => query.Where(p =>
                    (p.Tarifa != null ? p.Tarifa.TotalCalculado : p.CostoEnvio) >= opciones.PrioridadAltaDesde),
                OrdersOptions.PrioridadMedia => query.Where(p =>
                    (p.Tarifa != null ? p.Tarifa.TotalCalculado : p.CostoEnvio) >= opciones.PrioridadMediaDesde &&
                    (p.Tarifa != null ? p.Tarifa.TotalCalculado : p.CostoEnvio) < opciones.PrioridadAltaDesde),
                _ => query.Where(p =>
                    (p.Tarifa != null ? p.Tarifa.TotalCalculado : p.CostoEnvio) < opciones.PrioridadMediaDesde)
            };
        }

        if (filtro.EsperaMin is > 0)
        {
            // Espera mayor o igual a N minutos equivale a haber nacido antes del corte.
            var corteEspera = ahora.AddMinutes(-filtro.EsperaMin.Value);
            query = query.Where(p => p.FechaCreacion <= corteEspera);
        }

        // El total es el de la lista completa: la paginación no lo recorta.
        var total = await query.CountAsync();

        // Orden por defecto: valor descendente (primero los de valor más alto) y, a igual
        // valor, los que más llevan esperando (fecha de creación más antigua primero).
        var pedidos = await query
            .OrderByDescending(p => p.Tarifa != null ? p.Tarifa.TotalCalculado : p.CostoEnvio)
            .ThenBy(p => p.FechaCreacion)
            .Skip((filtro.Pagina - 1) * filtro.Tamano)
            .Take(filtro.Tamano)
            .ToListAsync();

        var items = pedidos.Select(p =>
        {
            var valor = p.Tarifa?.TotalCalculado ?? p.CostoEnvio;
            return new PedidoSinAsignarItem(
                PedidoId: p.Id,
                Codigo: p.Codigo,
                Restaurante: p.Restaurante.NombreComercial,
                RecogerEn: $"{p.Restaurante.DireccionSede}, {p.Restaurante.Ciudad}",
                EntregarEn: $"{p.DireccionEntrega.Calle} {p.DireccionEntrega.Numero}, {p.DireccionEntrega.Ciudad}",
                GeneradoEn: p.FechaCreacion,
                MinutosEsperando: (int)Math.Max(0, (ahora - p.FechaCreacion).TotalMinutes),
                Prioridad: opciones.PrioridadPara(valor),
                ValorDomicilio: valor,
                Zona: p.Restaurante.Ciudad,
                Estado: p.Estado);
        }).ToList();

        return new ListaPedidosSinAsignarResponse(
            Items: items,
            Total: total,
            Pagina: filtro.Pagina,
            Tamano: filtro.Tamano);
    }

    public async Task<RespuestaServiciosFinalizados> ObtenerServiciosFinalizadosAsync(FiltroServiciosFinalizados filtro)
    {
        var (desde, hasta) = VentanaDelHistorial(filtro);

        // UN solo conjunto: de acá salen las filas, el total y los KPIs. Por eso el contador
        // de la cabecera no puede discrepar de la lista: no hay una segunda consulta que se
        // pueda desincronizar. La comparación con ayer es el mismo constructor con la ventana
        // corrida un día, así que también lleva los mismos filtros.
        var conjunto = AplicarVentana(ConstruirConjuntoFinalizado(filtro), desde, hasta);
        var conjuntoAyer = AplicarVentana(
            ConstruirConjuntoFinalizado(filtro), desde.AddDays(-1), hasta.AddDays(-1));

        var total = await conjunto.CountAsync();
        var items = await ProyectarPaginaAsync(conjunto, filtro.Pagina, filtro.Tamano);
        var resumen = await ResumirAsync(conjunto, conjuntoAyer);

        return new RespuestaServiciosFinalizados(
            Pagina: new PaginaServiciosFinalizados(items, total, filtro.Pagina, filtro.Tamano),
            Resumen: resumen);
    }

    /// <summary>
    /// La ventana del rango. Sin <c>Desde</c> arranca hoy a las 00:00 UTC y dura un día: es
    /// lo que hace que <c>CompletadosHoy</c> sea hoy. <c>Hasta</c> es exclusivo.
    /// </summary>
    private static (DateTime Desde, DateTime Hasta) VentanaDelHistorial(FiltroServiciosFinalizados filtro)
    {
        var desde = filtro.Desde ?? DateTime.UtcNow.Date;
        return (desde, filtro.Hasta ?? desde.AddDays(1));
    }

    private static IQueryable<Pedido> AplicarVentana(IQueryable<Pedido> query, DateTime desde, DateTime hasta) =>
        query.Where(p => p.FechaEntrega >= desde && p.FechaEntrega < hasta);

    /// <summary>
    /// El conjunto de servicios finalizados: los pedidos entregados que cumplen los filtros.
    /// Acá van solo los filtros; la ventana de fechas la aplica <see cref="AplicarVentana"/>,
    /// para que el mismo conjunto se pueda correr con la ventana de ayer.
    /// </summary>
    private IQueryable<Pedido> ConstruirConjuntoFinalizado(FiltroServiciosFinalizados filtro)
    {
        // El tipo se declara explícito a propósito: `var` sobre un `Include` infiere
        // IIncludableQueryable, y después no se le puede asignar el resultado de un Where.
        IQueryable<Pedido> query = _db.Pedidos.AsNoTracking()
            .Include(p => p.Restaurante)
            .Include(p => p.DireccionEntrega)
            .Include(p => p.Tarifa)
            // Un servicio finalizado es un pedido entregado: es el único estado en el que la
            // FechaEntrega existe, y es la fecha del ciclo que ordena el historial.
            .Where(p => p.Estado == EstadosPedido.Entregado && p.FechaEntrega != null);

        if (filtro.RestauranteId is { } restauranteId)
        {
            query = query.Where(p => p.RestauranteId == restauranteId);
        }

        // La zona es la ciudad de la dirección de entrega: es lo que muestra la columna.
        if (!string.IsNullOrWhiteSpace(filtro.Zona))
        {
            var zona = filtro.Zona.Trim().ToLower();
            query = query.Where(p => p.DireccionEntrega.Ciudad.ToLower() == zona);
        }

        if (filtro.RepartidorId is { } repartidorId)
        {
            // El domiciliario de la fila es el del intento vigente —el último que no fue
            // rechazado, la misma regla que AsignacionesDelPedido— y no cualquiera que haya
            // pasado por el pedido: si no, el filtro traería filas que muestran a otro.
            query = query.Where(p => _db.AsignacionesReplicadas
                .Where(a => a.PedidoId == p.Id && a.EstadoAsignacion != AsignacionReplicada.EstadoRechazado)
                .OrderByDescending(a => a.FechaAsignacion)
                .Select(a => a.RepartidorId)
                .FirstOrDefault() == repartidorId);
        }

        if (filtro.CalificacionMin is { } minima)
        {
            // La calificación del servicio es la del restaurante (RatingGeneralComida), la
            // misma que promedia la cabecera. Un servicio sin encuesta no alcanza ninguna
            // mínima: no se lo inventa.
            query = query.Where(p => _db.CalificacionesReplicadas
                .Where(c => c.PedidoId == p.Id)
                .Select(c => (int?)c.CalificacionRestaurante)
                .FirstOrDefault() >= minima);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var patron = $"%{filtro.Buscar.Trim()}%";

            // Texto libre sobre lo que la fila muestra: el código del pedido, el nombre del
            // cliente y la dirección de entrega.
            query = query.Where(p =>
                EF.Functions.ILike(p.Codigo, patron) ||
                EF.Functions.ILike(p.DireccionEntrega.Calle, patron) ||
                _db.ContactosPedido.Any(c => c.PedidoId == p.Id
                                             && c.Tipo == TipoContactoCliente
                                             && EF.Functions.ILike(c.Nombre, patron)));
        }

        return query;
    }

    /// <summary>
    /// La página del historial, ordenada de lo más reciente a lo más viejo.
    ///
    /// Los cruces se resuelven en memoria sobre los ids de la página: la asignación es 1:N y
    /// elegir el intento vigente es una regla, no un join. Traerla como subconsulta por fila
    /// repetiría esa regla en cada columna y la dejaría a merced de un cambio en una sola.
    /// </summary>
    private async Task<List<ServicioFinalizado>> ProyectarPaginaAsync(
        IQueryable<Pedido> conjunto,
        int pagina,
        int tamano)
    {
        var pedidos = await conjunto
            .OrderByDescending(p => p.FechaEntrega)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync();

        if (pedidos.Count == 0)
        {
            return [];
        }

        var pedidoIds = pedidos.Select(p => p.Id).ToList();

        var asignaciones = await _db.AsignacionesReplicadas.AsNoTracking()
            .Where(a => pedidoIds.Contains(a.PedidoId))
            .ToListAsync();

        var calificaciones = await _db.CalificacionesReplicadas.AsNoTracking()
            .Where(c => pedidoIds.Contains(c.PedidoId))
            .ToListAsync();

        var repartidorIds = asignaciones.Select(a => a.RepartidorId).Distinct().ToList();

        var repartidores = await _db.RepartidoresReplicados.AsNoTracking()
            .Where(r => repartidorIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id);

        // El promedio histórico del domiciliario: todas sus encuestas, no la del pedido que
        // muestra la fila.
        var promedios = await _db.CalificacionesReplicadas.AsNoTracking()
            .Where(c => c.RepartidorId != null && repartidorIds.Contains(c.RepartidorId.Value))
            .GroupBy(c => c.RepartidorId!.Value)
            .Select(g => new
            {
                RepartidorId = g.Key,
                Promedio = g.Average(c => (double)c.CalificacionDomiciliario)
            })
            .ToDictionaryAsync(x => x.RepartidorId, x => x.Promedio);

        return pedidos.Select(p =>
        {
            var asignacion = AsignacionesDelPedido.Vigente(asignaciones, p.Id);
            var calificacion = calificaciones.FirstOrDefault(c => c.PedidoId == p.Id);

            return new ServicioFinalizado(
                PedidoId: p.Id,
                Codigo: p.Codigo,
                CerradoEn: p.FechaEntrega!.Value,
                Restaurante: new RestauranteDelServicio(
                    Id: p.RestauranteId,
                    Nombre: p.Restaurante.NombreComercial,
                    Ciudad: p.Restaurante.Ciudad),
                Domiciliario: asignacion is null
                    ? null
                    : new DomiciliarioDelServicio(
                        Id: asignacion.RepartidorId,
                        // El perfil del domiciliario llega por su propio evento y puede no
                        // estar todavía: se muestra lo que hay, no un nombre inventado.
                        Nombre: repartidores.TryGetValue(asignacion.RepartidorId, out var repartidor)
                            ? repartidor.NombreCompleto
                            : string.Empty,
                        Calificacion: promedios.TryGetValue(asignacion.RepartidorId, out var promedio)
                            ? promedio
                            : null),
                Origen: new OrigenDelServicio(p.Restaurante.DireccionSede),
                Destino: new DestinoDelServicio(
                    Direccion: $"{p.DireccionEntrega.Calle} {p.DireccionEntrega.Numero}".Trim(),
                    Zona: p.DireccionEntrega.Ciudad),
                Tiempos: new TiemposDelServicio(
                    AsignadoEn: asignacion?.FechaAsignacion,
                    // El hito del local: cuando el domiciliario escaneó el QR del restaurante.
                    RecogidoEn: asignacion?.FechaLlegadaLocal,
                    EntregadoEn: p.FechaEntrega.Value,
                    MinutosTotales: MinutosDelServicio(asignacion?.FechaAsignacion, p.FechaEntrega)),
                Valor: new ValorDelServicio(
                    Domicilio: ValorDelDomicilio(p),
                    Total: p.Total,
                    // Sin fuente: el plan todavía tiene que decidir de dónde sale quién paga.
                    Paga: null),
                Calificaciones: new CalificacionesDelServicio(
                    Domiciliario: calificacion?.CalificacionDomiciliario,
                    Restaurante: calificacion?.CalificacionRestaurante),
                // Sin modelo de incidencias en ningún servicio: el campo viaja para que la
                // pantalla no cambie cuando exista.
                Incidencia: new IncidenciaDelServicio(Hay: false, Motivo: null),
                Estado: p.Estado);
        }).ToList();
    }

    /// <summary>
    /// Los KPIs de la cabecera, calculados sobre el MISMO conjunto que devuelve las filas.
    /// No hay una consulta "parecida" para los números: si los filtros cambian, cambian para
    /// los dos, que es lo que evita el contador que dice un número mientras la lista muestra otro.
    /// </summary>
    private async Task<ResumenServiciosFinalizados> ResumirAsync(
        IQueryable<Pedido> conjunto,
        IQueryable<Pedido> conjuntoAyer)
    {
        var completados = await conjunto.CountAsync();

        // El valor de los domicilios es la suma del mismo campo que muestra la columna de
        // valor de cada fila.
        var valorDomicilios = await conjunto.SumAsync(
            p => p.Tarifa != null ? p.Tarifa.TotalCalculado : p.CostoEnvio);

        // La nota del servicio es la del restaurante (RatingGeneralComida), igual que en el
        // panel de métricas: promediar los dos sujetos —domiciliario y restaurante— daría un
        // número que no significa nada. Un pedido sin encuesta no entra en el promedio ni en
        // el conteo.
        var calificaciones = conjunto.Select(p => _db.CalificacionesReplicadas
            .Where(c => c.PedidoId == p.Id)
            .Select(c => (int?)c.CalificacionRestaurante)
            .FirstOrDefault());

        var calificacionesContadas = await calificaciones.CountAsync(c => c != null);
        var calificacionPromedio = calificacionesContadas == 0
            ? null
            : await calificaciones.AverageAsync(c => (double?)c);

        return new ResumenServiciosFinalizados(
            CompletadosHoy: completados,
            ValorDomiciliosHoy: valorDomicilios,
            CalificacionPromedio: calificacionPromedio,
            CalificacionesContadas: calificacionesContadas,
            // Sin modelo de incidencias no hay conteo que dar: null, no 0. Un 0 diría que no
            // hubo ninguna, y lo que pasa es que no se puede saber.
            ConIncidencias: null,
            TiempoPromedioMin: await TiempoPromedioAsync(conjunto),
            // La variación contra ayer corre el mismo agregado con la ventana corrida un día
            // y los mismos filtros.
            Ayer: new AyerDelResumen(
                Completados: await conjuntoAyer.CountAsync(),
                ValorDomicilios: await conjuntoAyer.SumAsync(
                    p => p.Tarifa != null ? p.Tarifa.TotalCalculado : p.CostoEnvio),
                TiempoPromedioMin: await TiempoPromedioAsync(conjuntoAyer)));
    }

    /// <summary>
    /// El tiempo promedio del servicio, sobre el mismo conjunto.
    ///
    /// La duración la calcula <see cref="MinutosDelServicio"/>, el mismo helper que usa cada
    /// fila: acá solo se materializan los dos extremos del cálculo —no la fila entera— porque
    /// la resta de fechas en minutos no se traduce a SQL de forma portable.
    /// </summary>
    private async Task<int?> TiempoPromedioAsync(IQueryable<Pedido> conjunto)
    {
        var extremos = await conjunto
            .Select(p => new
            {
                p.FechaEntrega,
                // El intento vigente: la misma regla que aplica la fila.
                AsignadoEn = _db.AsignacionesReplicadas
                    .Where(a => a.PedidoId == p.Id && a.EstadoAsignacion != AsignacionReplicada.EstadoRechazado)
                    .OrderByDescending(a => a.FechaAsignacion)
                    .Select(a => (DateTime?)a.FechaAsignacion)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var minutos = extremos
            .Select(e => MinutosDelServicio(e.AsignadoEn, e.FechaEntrega))
            .OfType<int>()
            .ToList();

        // Un promedio que no se puede calcular no viaja en 0: 0 minutos diría que el servicio
        // se entregó en el acto.
        return minutos.Count == 0 ? null : (int)Math.Round(minutos.Average());
    }

    /// <summary>
    /// La duración del servicio, del momento en que se asignó al momento en que se entregó.
    ///
    /// Es el ÚNICO lugar donde se calcula: la usan la columna de cada fila y el KPI de tiempo
    /// promedio, así que los dos no pueden diferir. Null si falta un extremo —un servicio
    /// entregado sin asignación registrada no tiene duración— y no 0.
    /// </summary>
    private static int? MinutosDelServicio(DateTime? asignadoEn, DateTime? entregadoEn) =>
        asignadoEn is null || entregadoEn is null
            ? null
            : (int)Math.Round((entregadoEn.Value - asignadoEn.Value).TotalMinutes);

    /// <summary>
    /// El valor del domicilio: la tarifa aplicada y, si el pedido no tiene tarifa, el costo de
    /// envío que quedó en el pedido. Es el mismo campo que suman la columna y el KPI.
    /// </summary>
    private static decimal ValorDelDomicilio(Pedido pedido) =>
        pedido.Tarifa?.TotalCalculado ?? pedido.CostoEnvio;

    private async Task<string> GenerarCodigoPedidoAsync()
    {
        // Formato: PED-XXXXXX (6 dígitos)
        var today = DateTime.UtcNow;
        var prefix = $"PED-{today:yyyyMMdd}";

        var lastCode = await _db.Pedidos
            .Where(p => p.Codigo.StartsWith(prefix))
            .OrderByDescending(p => p.Codigo)
            .Select(p => p.Codigo)
            .FirstOrDefaultAsync();

        int sequence = 1;
        if (!string.IsNullOrEmpty(lastCode))
        {
            var lastSequence = lastCode.Substring(prefix.Length + 1); // PED-YYYYMMDD-XXXXXX
            if (int.TryParse(lastSequence, out var parsed))
            {
                sequence = parsed + 1;
            }
        }

        return $"{prefix}-{sequence:D6}";
    }

    /// <summary>
    /// Token QR: UUID v4 en hexadecimal sin guiones. Se generan DOS por pedido y no pueden
    /// repetirse entre sí — cada uno lo escanea una parte distinta (FR-002.5).
    /// </summary>
    private string GenerarTokenQr()
    {
        return Guid.NewGuid().ToString("N")[..32]; // 32 chars hex
    }

    private bool EstaEnHorarioAtencion(RestauranteReplicado restaurante)
    {
        var now = TimeOnly.FromDateTime(DateTime.UtcNow); // Asumir UTC, ajustar según zona horaria del restaurante
        var diaSemana = DateTime.UtcNow.DayOfWeek.ToString(); // Monday, Tuesday, etc.

        var horario = restaurante.HorariosAtencion
            .FirstOrDefault(h => h.DiaSemana.Equals(diaSemana, StringComparison.OrdinalIgnoreCase) && !h.Cerrado);

        if (horario == null) return false;

        return now >= TimeOnly.FromTimeSpan(horario.HoraInicio) && now <= TimeOnly.FromTimeSpan(horario.HoraFin);
    }

    private PedidoResponse MapToResponse(Pedido pedido)
    {
        return new PedidoResponse(
            Id: pedido.Id,
            ClienteId: pedido.ClienteId,
            RestauranteId: pedido.RestauranteId,
            Codigo: pedido.Codigo,
            Estado: pedido.Estado,
            Subtotal: pedido.Subtotal,
            CostoEnvio: pedido.CostoEnvio,
            Total: pedido.Total,
            DireccionEntrega: new DireccionEntregaResponse(
                Id: pedido.DireccionEntrega.Id,
                Calle: pedido.DireccionEntrega.Calle,
                Numero: pedido.DireccionEntrega.Numero,
                Interior: pedido.DireccionEntrega.Interior,
                Referencia: pedido.DireccionEntrega.Referencia,
                Ciudad: pedido.DireccionEntrega.Ciudad,
                Departamento: pedido.DireccionEntrega.Departamento,
                CodigoPostal: pedido.DireccionEntrega.CodigoPostal,
                Latitud: (decimal)pedido.DireccionEntrega.Ubicacion.Y,
                Longitud: (decimal)pedido.DireccionEntrega.Ubicacion.X
            ),
            TokenQrLocal: pedido.TokenQrLocal,
            NotasCliente: pedido.NotasCliente,
            NotasRestaurante: pedido.NotasRestaurante,
            FechaCreacion: pedido.FechaCreacion,
            FechaConfirmacion: pedido.FechaConfirmacion,
            FechaPreparacion: pedido.FechaPreparacion,
            FechaListo: pedido.FechaListo,
            FechaEntrega: pedido.FechaEntrega,
            FechaCancelacion: pedido.FechaCancelacion,
            Items: pedido.Items.Select(i => new ItemPedidoResponse(
                Id: i.Id,
                NombreProducto: i.NombreProducto,
                Descripcion: i.Descripcion,
                Cantidad: i.Cantidad,
                PrecioUnitario: i.PrecioUnitario,
                Subtotal: i.Subtotal
            )).ToList(),
            Contactos: pedido.Contactos.Select(c => new ContactoPedidoResponse(
                Id: c.Id,
                Tipo: c.Tipo,
                Nombre: c.Nombre,
                Telefono: c.Telefono,
                Email: c.Email
            )).ToList(),
            Tarifa: pedido.Tarifa != null ? new TarifaAplicadaResponse(
                Id: pedido.Tarifa.Id,
                TipoTarifa: pedido.Tarifa.TipoTarifa,
                CostoBase: pedido.Tarifa.CostoBase,
                CostoPorKm: pedido.Tarifa.CostoPorKm,
                DistanciaKm: pedido.Tarifa.DistanciaKm,
                CostoAdicional: pedido.Tarifa.CostoAdicional,
                DetalleCalculo: pedido.Tarifa.DetalleCalculo,
                TotalCalculado: pedido.Tarifa.TotalCalculado
            ) : null
        );
    }
}