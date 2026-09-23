using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Deuna.Identity.Service.DTOs;
using Deuna.Identity.Service.Models;
using Deuna.Identity.Service.Models.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Deuna.Identity.Service.Services;

/// <summary>
/// Emisión y validación de los códigos de autorización del alta de restaurantes
/// (FR-001.8 a FR-001.11).
///
/// El flujo es de dos pasos: primero se valida el código y se obtiene un token de
/// continuidad; recién entonces el restaurante envía sus datos. Así el código no viaja
/// junto con el formulario y el registro no puede completarse sin él.
/// </summary>
public interface IAuthorizationCodeService
{
    /// <summary>Emite un código nuevo para el área comercial.</summary>
    Task<AuthResponse> EmitAsync(EmitAuthorizationCodeRequest request, Guid? emitidoPorUsuarioId);

    /// <summary>Valida el código y, si corresponde, devuelve el token de continuidad.</summary>
    Task<AuthResponse> ValidateAsync(string codigo);

    /// <summary>Valida el token de continuidad y el estado del código al que pertenece.</summary>
    Task<(bool Ok, string Message, Guid? CodigoId)> ValidateContinuityTokenAsync(string token);

    /// <summary>Asocia el código al restaurante que completó el alta (un solo uso).</summary>
    Task<bool> ConsumeAsync(Guid codigoId, Guid restauranteId);
}

public class AuthorizationCodeService : IAuthorizationCodeService
{
    /// <summary>Claim que distingue este token de un access token: no sirve para autenticarse.</summary>
    public const string PropositoClaim = "proposito";
    public const string PropositoRegistro = "registro-restaurante";

    private static readonly TimeSpan VigenciaPorDefecto = TimeSpan.FromDays(30);
    private static readonly TimeSpan VigenciaDelTokenDeContinuidad = TimeSpan.FromMinutes(30);

    private readonly IdentityDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthorizationCodeService> _logger;

    public AuthorizationCodeService(IdentityDbContext db, IConfiguration config, ILogger<AuthorizationCodeService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task<AuthResponse> EmitAsync(EmitAuthorizationCodeRequest request, Guid? emitidoPorUsuarioId)
    {
        var vigencia = request.VigenciaDias is > 0
            ? TimeSpan.FromDays(request.VigenciaDias.Value)
            : VigenciaPorDefecto;

        var codigo = await GenerarCodigoUnicoAsync();

        var entidad = new CodigoAutorizacion
        {
            Codigo = codigo,
            Estado = EstadoCodigoAutorizacion.Disponible,
            FechaEmision = DateTime.UtcNow,
            FechaVencimiento = DateTime.UtcNow.Add(vigencia),
            EmitidoPorUsuarioId = emitidoPorUsuarioId,
            Notas = request.Notas
        };

        _db.CodigosAutorizacion.Add(entidad);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Código de autorización emitido: {CodigoId}", entidad.Id);

        return new AuthResponse(true, "Código de autorización emitido", new
        {
            codigo = entidad.Codigo,
            estado = entidad.Estado,
            fechaEmision = entidad.FechaEmision,
            fechaVencimiento = entidad.FechaVencimiento,
            notas = entidad.Notas
        });
    }

    public async Task<AuthResponse> ValidateAsync(string codigo)
    {
        var ahora = DateTime.UtcNow;
        var entidad = await _db.CodigosAutorizacion.FirstOrDefaultAsync(c => c.Codigo == codigo);

        if (entidad == null)
        {
            return new AuthResponse(false, "El código de autorización no es válido. Contacte al área comercial.");
        }

        if (entidad.Estado == EstadoCodigoAutorizacion.Usado)
        {
            return new AuthResponse(false, "El código ya fue utilizado. Cada código habilita un único registro.");
        }

        if (entidad.EstaVencido(ahora))
        {
            return new AuthResponse(false, "El código venció. Contacte al área comercial para recibir uno nuevo.");
        }

        // Se marca usado al validarlo: un código habilita un solo intento de alta.
        entidad.Estado = EstadoCodigoAutorizacion.Usado;
        entidad.FechaUso = ahora;
        await _db.SaveChangesAsync();

        var expira = ahora.Add(VigenciaDelTokenDeContinuidad);

        _logger.LogInformation("Código de autorización validado: {CodigoId}", entidad.Id);

        return new AuthResponse(true, "Código válido. Continúe con el registro.", new
        {
            continuidadToken = GenerarTokenDeContinuidad(entidad.Id, expira),
            codigoId = entidad.Id,
            expiraEn = expira
        });
    }

    public async Task<(bool Ok, string Message, Guid? CodigoId)> ValidateContinuityTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "El código de autorización es obligatorio para registrarse.", null);
        }

        ClaimsPrincipal principal;
        try
        {
            principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ValidIssuer = _config["Jwt:Issuer"] ?? "Deuna.Identity",
                ValidAudience = _config["Jwt:Audience"] ?? "Deuna.Api",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(GetJwtSecret()))
            }, out _);
        }
        catch (Exception ex) when (ex is SecurityTokenException or SecurityTokenMalformedException or ArgumentException)
        {
            // Un token malformado es un 400, nunca un 500: es entrada de un endpoint público.
            return (false, "El código de autorización no es válido. Contacte al área comercial.", null);
        }

        // Un access token no sirve aquí: el propósito tiene que ser el registro.
        if (principal.FindFirst(PropositoClaim)?.Value != PropositoRegistro)
        {
            return (false, "El código de autorización no es válido. Contacte al área comercial.", null);
        }

        var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(sub, out var codigoId))
        {
            return (false, "El código de autorización no es válido. Contacte al área comercial.", null);
        }

        var entidad = await _db.CodigosAutorizacion.FirstOrDefaultAsync(c => c.Id == codigoId);
        if (entidad == null)
        {
            return (false, "El código de autorización no es válido. Contacte al área comercial.", null);
        }

        if (entidad.RestauranteId != null)
        {
            return (false, "El código ya fue utilizado. Cada código habilita un único registro.", null);
        }

        return (true, "Código válido", entidad.Id);
    }

    public async Task<bool> ConsumeAsync(Guid codigoId, Guid restauranteId)
    {
        var entidad = await _db.CodigosAutorizacion.FirstOrDefaultAsync(c => c.Id == codigoId);
        if (entidad == null || entidad.RestauranteId != null)
        {
            return false;
        }

        entidad.RestauranteId = restauranteId;
        await _db.SaveChangesAsync();
        return true;
    }

    private async Task<string> GenerarCodigoUnicoAsync()
    {
        for (var intento = 0; intento < 5; intento++)
        {
            var candidato = GenerarCodigo();
            if (!await _db.CodigosAutorizacion.AnyAsync(c => c.Codigo == candidato))
            {
                return candidato;
            }
        }

        throw new InvalidOperationException("No se pudo generar un código de autorización único");
    }

    /// <summary>Código legible: sin caracteres ambiguos (O/0, I/1) para poder dictarlo.</summary>
    private static string GenerarCodigo()
    {
        const string alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var caracteres = new char[10];
        for (var i = 0; i < caracteres.Length; i++)
        {
            caracteres[i] = alfabeto[RandomNumberGenerator.GetInt32(alfabeto.Length)];
        }

        return $"DEUNA-{new string(caracteres, 0, 5)}-{new string(caracteres, 5, 5)}";
    }

    private string GenerarTokenDeContinuidad(Guid codigoId, DateTime expira)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(GetJwtSecret()));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "Deuna.Identity",
            audience: _config["Jwt:Audience"] ?? "Deuna.Api",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, codigoId.ToString()),
                new Claim(PropositoClaim, PropositoRegistro)
            ],
            expires: expira,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string GetJwtSecret() =>
        _config["Jwt:SecretKey"] ?? "DeunaSuperSecretKey2026!@#$%^&*()_+-=[]{}|;:,.<>?";
}
