using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Deuna.Notification.Service.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deuna.Notification.Service.Tests;

/// <summary>
/// El proveedor real es FCM HTTP v1: primero canjea la cuenta de servicio por un access
/// token, y después publica el mensaje. Se verifica el request que construye con un
/// handler HTTP falso: es lo único comprobable sin credenciales de Firebase.
/// </summary>
public class FcmPushSenderTests
{
    private const string ProjectId = "deuna-test";

    private static FcmOptions Opciones() =>
        new()
        {
            ProjectId = ProjectId,
            ServiceAccountJson = CuentaDeServicioFalsa()
        };

    /// <summary>Cuenta de servicio con una clave RSA generada en el momento.</summary>
    private static string CuentaDeServicioFalsa()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportRSAPrivateKeyPem().Replace("\n", "\\n");

        return $$"""
        {
          "type": "service_account",
          "project_id": "{{ProjectId}}",
          "private_key_id": "fake",
          "private_key": "{{pem}}",
          "client_email": "fcm@{{ProjectId}}.iam.gserviceaccount.com",
          "token_uri": "https://oauth2.googleapis.com/token"
        }
        """;
    }

    private static FcmPushSender CrearSender(HandlerFalso handler) =>
        new(new HttpClient(handler), Opciones(), NullLogger<FcmPushSender>.Instance);

    private static MensajePush Mensaje() => new(
        Titulo: "Pedido asignado",
        Cuerpo: "PED-00042 · a 999 m",
        Datos: new Dictionary<string, string> { ["pedidoId"] = "abc-123", ["tipo"] = "PedidoAsignado" });

    [Fact]
    public async Task EnviaElMensajeAlEndpointDelProyectoConElAccessToken()
    {
        var handler = new HandlerFalso();
        var sender = CrearSender(handler);

        var resultado = await sender.EnviarAsync("token-del-dispositivo", Mensaje());

        resultado.Exito.Should().BeTrue();

        // 1) canje de la cuenta de servicio por un access token
        // 2) envío del mensaje
        handler.Peticiones.Should().HaveCount(2);

        var oauth = handler.Peticiones[0];
        oauth.Request.RequestUri!.Host.Should().Be("oauth2.googleapis.com");
        oauth.Body.Should().Contain("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer");

        var envio = handler.Peticiones[1];
        envio.Request.Method.Should().Be(HttpMethod.Post);
        envio.Request.RequestUri!.AbsoluteUri.Should()
            .Be($"https://fcm.googleapis.com/v1/projects/{ProjectId}/messages:send");
        envio.Request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        envio.Request.Headers.Authorization.Parameter.Should().Be("access-token-falso");
    }

    [Fact]
    public async Task ElPayloadLlevaElTokenElTituloElCuerpoYLosDatos()
    {
        var handler = new HandlerFalso();
        var sender = CrearSender(handler);

        await sender.EnviarAsync("token-del-dispositivo", Mensaje());

        using var payload = JsonDocument.Parse(handler.Peticiones[1].Body);
        var message = payload.RootElement.GetProperty("message");

        message.GetProperty("token").GetString().Should().Be("token-del-dispositivo");
        message.GetProperty("notification").GetProperty("title").GetString().Should().Be("Pedido asignado");
        message.GetProperty("notification").GetProperty("body").GetString().Should().Be("PED-00042 · a 999 m");

        // Los datos son lo que la app usa para navegar al pedido al tocar la notificación.
        var datos = message.GetProperty("data");
        datos.GetProperty("pedidoId").GetString().Should().Be("abc-123");
        datos.GetProperty("tipo").GetString().Should().Be("PedidoAsignado");
    }

    [Fact]
    public async Task UnTokenNoRegistrado_MarcaElTokenComoInvalido()
    {
        var handler = new HandlerFalso
        {
            RespuestaEnvio = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""
                {"error":{"status":"NOT_FOUND","details":[{"errorCode":"UNREGISTERED"}]}}
                """)
            }
        };

        var resultado = await CrearSender(handler).EnviarAsync("token-viejo", Mensaje());

        resultado.Exito.Should().BeFalse();
        resultado.TokenInvalido.Should().BeTrue("FCM dice que el token ya no existe");
    }

    [Fact]
    public async Task UnErrorDelProveedor_NoMarcaElTokenComoInvalido()
    {
        var handler = new HandlerFalso
        {
            RespuestaEnvio = new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("""{"error":{"status":"INTERNAL"}}""")
            }
        };

        var resultado = await CrearSender(handler).EnviarAsync("token-bueno", Mensaje());

        resultado.Exito.Should().BeFalse();
        resultado.TokenInvalido.Should().BeFalse("un 500 del proveedor no dice nada del token");
        resultado.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task UnFalloAlObtenerElAccessToken_NoRompeElSiguienteEnvio()
    {
        // Regresión: si la clave privada se crea y se libera en cada firma, el segundo
        // intento reusa el proveedor de firma cacheado por material de clave —que apunta
        // a la RSA ya liberada— y falla con "Cannot access a disposed object".
        // Con el access token cacheado el caso no aparecería hasta que venza, así que se
        // fuerza el fallo del primer canje para obligar a firmar dos veces.
        var handler = new HandlerFalso { FallarOauth = 1 };
        var sender = CrearSender(handler);

        var primero = await sender.EnviarAsync("token-1", Mensaje());
        var segundo = await sender.EnviarAsync("token-2", Mensaje());

        primero.Exito.Should().BeFalse("el primer canje del token falla a propósito");
        segundo.Exito.Should().BeTrue("la clave de la cuenta de servicio sigue viva entre envíos");
        segundo.Error.Should().BeNull();
    }

    /// <summary>Handler HTTP falso: responde distinto al canje OAuth y al envío.</summary>
    private sealed class HandlerFalso : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Peticiones { get; } = [];
        public HttpResponseMessage? RespuestaEnvio { get; set; }

        /// <summary>Cantidad de canjes OAuth que deben fallar antes de empezar a responder bien.</summary>
        public int FallarOauth { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Peticiones.Add((request, body));

            if (request.RequestUri!.Host.Contains("oauth2"))
            {
                if (FallarOauth > 0)
                {
                    FallarOauth--;
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("""{"error":"invalid_grant"}""")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"access-token-falso","expires_in":3600,"token_type":"Bearer"}""")
                };
            }

            return RespuestaEnvio ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"name":"projects/deuna-test/messages/1"}""")
            };
        }
    }
}
