using Dapper;
using Npgsql;

namespace Deuna.Metrics.Service.Services;

/// <summary>
/// Acceso de solo lectura a una base.
///
/// Cada subservicio de dominio construye la suya con su cadena de conexión, así
/// que no hay forma de que una consulta de pedidos termine leyendo la base de
/// feedback: la conexión que tiene en la mano es una sola.
///
/// Es de solo lectura por contrato, no por permisos todavía: todas las consultas
/// son SELECT. La recomendación para producción es un usuario de base con permisos
/// únicamente de lectura, para que eso deje de depender de la disciplina.
/// </summary>
public sealed class FuenteDatos(string cadenaConexion)
{
    /// <summary>Filas de una consulta.</summary>
    public async Task<IReadOnlyList<T>> ConsultarAsync<T>(
        string sql,
        object? parametros = null,
        CancellationToken cancelacion = default)
    {
        await using var conexion = new NpgsqlConnection(cadenaConexion);
        var filas = await conexion.QueryAsync<T>(
            new CommandDefinition(sql, parametros, cancellationToken: cancelacion));

        return filas.AsList();
    }

    /// <summary>Una sola fila, o <c>null</c> si la consulta no devolvió nada.</summary>
    public async Task<T?> ConsultarUnaAsync<T>(
        string sql,
        object? parametros = null,
        CancellationToken cancelacion = default)
    {
        await using var conexion = new NpgsqlConnection(cadenaConexion);
        return await conexion.QuerySingleOrDefaultAsync<T>(
            new CommandDefinition(sql, parametros, cancellationToken: cancelacion));
    }
}
