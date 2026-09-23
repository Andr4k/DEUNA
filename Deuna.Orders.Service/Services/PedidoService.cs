using System.Security.Cryptography;
using Deuna.Orders.Service.Models;
using Deuna.Orders.Service.DTOs;
using Deuna.Orders.Service.Events;
using Deuna.Shared.Extensions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
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
}

public class PedidoService : IPedidoService
{
    private readonly OrdersDbContext _db;
    private readonly ITarifaService _tarifaService;
    private readonly IGeoService _geoService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<PedidoService> _logger;

    public PedidoService(
        OrdersDbContext db,
        ITarifaService tarifaService,
        IGeoService geoService,
        IPublishEndpoint publishEndpoint,
        ILogger<PedidoService> logger)
    {
        _db = db;
        _tarifaService = tarifaService;
        _geoService = geoService;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<CrearPedidoResponse> CrearPedidoAsync(CrearPedidoRequest request, HttpContext httpContext)
    {
        // Obtener ClienteId desde el JWT (rol RESTAURANT)
        var clienteId = httpContext.GetUserId() ?? throw new UnauthorizedAccessException("Usuario no autenticado");
        var rol = httpContext.GetRole();

        if (rol != "RESTAURANT")
        {
            throw new UnauthorizedAccessException("Solo los restaurantes pueden crear pedidos");
        }

        // Verificar que el restaurante existe y está activo
        var restaurante = await _db.RestaurantesReplicados
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

        // Generar QR UUID
        var qrCodigo = GenerarQrCodigo();

        // Crear pedido principal
        var pedido = new Pedido
        {
            ClienteId = clienteId,
            RestauranteId = request.RestauranteId,
            Codigo = codigo,
            Estado = "Pendiente",
            Subtotal = subtotal,
            CostoEnvio = tarifa.TotalCalculado,
            Total = subtotal + tarifa.TotalCalculado,
            DireccionEntregaId = direccionEntrega.Id,
            QrCodigo = qrCodigo,
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
            QrCodigo: pedido.QrCodigo,
            OccurredAt: DateTime.UtcNow
        );

        await _publishEndpoint.Publish(evento);

        _logger.LogInformation("Pedido creado: {Codigo} (ID: {PedidoId}) para restaurante {RestauranteId}",
            pedido.Codigo, pedido.Id, pedido.RestauranteId);

        return new CrearPedidoResponse(
            PedidoId: pedido.Id,
            Codigo: pedido.Codigo,
            QrCodigo: pedido.QrCodigo,
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

    private string GenerarQrCodigo()
    {
        // UUID v4 para QR
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
            QrCodigo: pedido.QrCodigo,
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