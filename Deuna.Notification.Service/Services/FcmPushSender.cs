using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Deuna.Notification.Service.Services;

/// <summary>
/// Proveedor real: Firebase Cloud Messaging HTTP v1.
///
/// El flujo de la API v1 tiene dos pasos: se canjea la cuenta de servicio por un access
/// token de OAuth2 (firmando un JWT con la clave privada) y después se publica el mensaje
/// en <c>/v1/projects/{projectId}/messages:send</c>. El access token se cachea hasta su
/// vencimiento para no pedir uno por notificación.
/// </summary>
public class FcmPushSender : IPushSender, IDisposable
{
    public const string NombreProveedor = "fcm";

    private const string UrlTokenOAuth = "https://oauth2.googleapis.com/token";
    private const string ScopeFcm = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly HttpClient _http;
    private readonly FcmOptions _options;
    private readonly ILogger<FcmPushSender> _logger;

    private readonly SemaphoreSlim _candado = new(1, 1);
    private string? _accessToken;
    private DateTime _accessTokenExpira = DateTime.MinValue;

    /// <summary>
    /// La clave privada de la cuenta de servicio vive lo que vive el emisor.
    ///
    /// No se puede crear y liberar por envío: JwtSecurityTokenHandler cachea el proveedor
    /// de firma por material de clave, así que al segundo intento devuelve el proveedor
    /// construido sobre la RSA ya liberada y falla con "Cannot access a disposed object".
    /// </summary>
    private readonly RSA? _clavePrivada;
    private readonly SigningCredentials? _credenciales;
    private readonly string? _emailCuentaServicio;

    public FcmPushSender(HttpClient http, IOptions<FcmOptions> options, ILogger<FcmPushSender> logger)
        : this(http, options.Value, logger)
    {
    }

    public FcmPushSender(HttpClient http, FcmOptions options, ILogger<FcmPushSender> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;

        if (_options.EstaConfigurado)
        {
            (_clavePrivada, _credenciales, _emailCuentaServicio) = PrepararCredenciales();
        }
    }

    public async Task<ResultadoPush> EnviarAsync(string token, MensajePush mensaje, CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await ObtenerAccessTokenAsync(cancellationToken);
            var url = $"https://fcm.googleapis.com/v1/projects/{_options.ProjectId}/messages:send";

            // `data` es lo que la app usa para navegar al pedido cuando el usuario toca la
            // notificación; `notification` es lo que se muestra.
            var payload = new
            {
                message = new
                {
                    token,
                    notification = new { title = mensaje.Titulo, body = mensaje.Cuerpo },
                    data = mensaje.Datos
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, cancellationToken);
            var cuerpo = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new ResultadoPush(true, NombreProveedor);
            }

            var tokenInvalido = EsTokenInvalido(response.StatusCode, cuerpo);

            _logger.LogWarning(
                "FCM rechazó el envío ({Status}){Invalido}: {Cuerpo}",
                (int)response.StatusCode,
                tokenInvalido ? " — token no registrado" : string.Empty,
                Recortar(cuerpo));

            return new ResultadoPush(
                false,
                NombreProveedor,
                $"{(int)response.StatusCode} {Recortar(cuerpo)}",
                tokenInvalido);
        }
        catch (Exception ex)
        {
            // El proveedor puede fallar o colgarse: se devuelve el fallo, no se propaga.
            _logger.LogError(ex, "Error enviando la notificación a FCM");
            return new ResultadoPush(false, NombreProveedor, ex.Message);
        }
    }

    /// <summary>
    /// FCM responde 404 UNREGISTERED cuando el token ya no existe (app desinstalada,
    /// reinstalada o token rotado). Ese token hay que dejar de usar.
    /// </summary>
    private static bool EsTokenInvalido(System.Net.HttpStatusCode status, string cuerpo) =>
        status == System.Net.HttpStatusCode.NotFound
        || cuerpo.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase)
        || cuerpo.Contains("registration-token-not-registered", StringComparison.OrdinalIgnoreCase);

    private async Task<string> ObtenerAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTime.UtcNow < _accessTokenExpira)
        {
            return _accessToken;
        }

        await _candado.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTime.UtcNow < _accessTokenExpira)
            {
                return _accessToken;
            }

            using var contenido = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = CrearAssertion()
            });

            using var response = await _http.PostAsync(UrlTokenOAuth, contenido, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"No se pudo obtener el access token de FCM: {(int)response.StatusCode} {Recortar(json)}");
            }

            using var documento = JsonDocument.Parse(json);
            var accessToken = documento.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("La respuesta de OAuth no trajo access_token");

            var expiraEnSegundos = documento.RootElement.TryGetProperty("expires_in", out var expira)
                ? expira.GetInt32()
                : 3600;

            _accessToken = accessToken;
            // Margen de un minuto para no usar un token que vence mientras viaja la petición.
            _accessTokenExpira = DateTime.UtcNow.AddSeconds(Math.Max(60, expiraEnSegundos - 60));

            return accessToken;
        }
        finally
        {
            _candado.Release();
        }
    }

    private string CrearAssertion()
    {
        var ahora = DateTimeOffset.UtcNow;

        var assertion = new JwtSecurityToken(
            issuer: _emailCuentaServicio,
            audience: UrlTokenOAuth,
            claims: [new Claim("scope", ScopeFcm)],
            notBefore: ahora.UtcDateTime,
            expires: ahora.AddHours(1).UtcDateTime,
            signingCredentials: _credenciales);

        return new JwtSecurityTokenHandler().WriteToken(assertion);
    }

    /// <summary>Lee la cuenta de servicio una sola vez y deja la clave lista para firmar.</summary>
    private (RSA Clave, SigningCredentials Credenciales, string Email) PrepararCredenciales()
    {
        using var documento = JsonDocument.Parse(_options.ServiceAccountJson!);
        var email = documento.RootElement.GetProperty("client_email").GetString()
            ?? throw new InvalidOperationException("La cuenta de servicio no tiene client_email");
        var clavePem = documento.RootElement.GetProperty("private_key").GetString()
            ?? throw new InvalidOperationException("La cuenta de servicio no tiene private_key");

        var rsa = RSA.Create();
        rsa.ImportFromPem(clavePem);

        var credenciales = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);
        return (rsa, credenciales, email);
    }

    public void Dispose()
    {
        _clavePrivada?.Dispose();
        _candado.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string Recortar(string texto) =>
        texto.Length <= 300 ? texto : texto[..300];
}
