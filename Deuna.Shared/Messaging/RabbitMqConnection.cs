using Microsoft.Extensions.Configuration;

namespace Deuna.Shared.Messaging;

/// <summary>
/// Construye la cadena de conexión de RabbitMQ a partir de la configuración.
///
/// Motivo: los servicios leían <c>GetConnectionString("RabbitMQ")</c>, que nunca está
/// configurado (docker-compose define las claves <c>RabbitMQ:*</c>), así que caían en un
/// placeholder con la contraseña enmascarada y host <c>localhost</c>. El resultado era un
/// health check permanentemente Unhealthy y conexiones imposibles dentro de un contenedor.
/// </summary>
public static class RabbitMqConnection
{
    private const string HostPorDefecto = "localhost";
    private const int PuertoPorDefecto = 5672;
    private const string UsuarioPorDefecto = "deuna";
    private const string PasswordPorDefecto = "rabbitmq_dev_2026";
    private const string VirtualHostPorDefecto = "deuna";

    /// <summary>
    /// Devuelve la URI de conexión: primero <c>ConnectionStrings:RabbitMQ</c> si existe,
    /// y si no la arma con las claves <c>RabbitMQ:Host</c>, <c>Port</c>, <c>Username</c>,
    /// <c>Password</c> y <c>VirtualHost</c>.
    /// </summary>
    public static Uri BuildUri(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("RabbitMQ");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return new Uri(connectionString);
        }

        var host = configuration["RabbitMQ:Host"] ?? HostPorDefecto;
        var port = configuration.GetValue<int>("RabbitMQ:Port", PuertoPorDefecto);
        var username = configuration["RabbitMQ:Username"] ?? UsuarioPorDefecto;
        var password = configuration["RabbitMQ:Password"] ?? PasswordPorDefecto;
        var vhost = configuration["RabbitMQ:VirtualHost"] ?? VirtualHostPorDefecto;

        return new Uri($"amqp://{username}:{password}@{host}:{port}/{vhost}");
    }
}
