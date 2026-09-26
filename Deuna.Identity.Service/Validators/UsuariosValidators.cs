using Deuna.Identity.Service.DTOs;
using FluentValidation;

namespace Deuna.Identity.Service.Validators;

/// <summary>
/// Valida la FORMA del pedido del listado de usuarios: que el tipo sea uno de los que
/// existen y que la paginación esté dentro del tope. Quién puede pedir el listado lo
/// decide la verificación de permisos del endpoint, no este validador: son dos capas
/// distintas, y un pedido perfectamente bien formado de quien no tiene el permiso igual
/// tiene que quedar afuera.
/// </summary>
public class ConsultaUsuariosValidator : AbstractValidator<ConsultaUsuarios>
{
    public ConsultaUsuariosValidator()
    {
        // Se valida siempre que el parámetro venga, aunque venga vacío: un tipo presente
        // que no es un tipo válido tiene que fallar. Si se ignorara, la consulta saldría
        // sin filtro por tipo y devolvería la lista entera como si hubiera filtrado bien.
        RuleFor(x => x.Tipo)
            .Must(SerUnTipoQueExiste)
            .When(x => x.Tipo is not null)
            .WithMessage($"Tipo de usuario inválido. Valores: {string.Join(", ", ConsultaUsuarios.TiposValidos)}");

        RuleFor(x => x.Pagina)
            .GreaterThanOrEqualTo(1)
            .WithMessage("La página tiene que ser 1 o más");

        RuleFor(x => x.TamanoPagina)
            .InclusiveBetween(1, ConsultaUsuarios.TamanoPaginaMaximo)
            .WithMessage($"El tamaño de página tiene que estar entre 1 y {ConsultaUsuarios.TamanoPaginaMaximo}");
    }

    private static bool SerUnTipoQueExiste(string? tipo) =>
        !string.IsNullOrWhiteSpace(tipo)
        && ConsultaUsuarios.TiposValidos.Contains(tipo.Trim(), StringComparer.OrdinalIgnoreCase);
}
