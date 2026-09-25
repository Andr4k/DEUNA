using System.Security.Cryptography;
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
    Task<List<PedidoResponse>> ObtenerPedidosPorClienteAsync(Guid clienteId);
    Task<List<PedidoResponse>> ObtenerPedidosPorRestauranteAsync(Guid restauranteId);

    /// <summary>
    /// Listado paginado de pedidos sin asignar para el panel del administrador.
    /// De solo lectura: no cambia el estado de ningún pedido.
    /// </summary>
    Task<ListaPedidosSinAsignarResponse> ObtenerPedidosSinAsignarAsync(FiltroPedidosSinAsignar filtro);
}

public class PedidoService : IPedidoService
{
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