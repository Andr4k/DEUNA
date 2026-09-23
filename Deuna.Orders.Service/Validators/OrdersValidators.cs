using FluentValidation;
using Deuna.Orders.Service.DTOs;

namespace Deuna.Orders.Service.Validators;

public class CrearItemPedidoRequestValidator : AbstractValidator<CrearItemPedidoRequest>
{
    public CrearItemPedidoRequestValidator()
    {
        RuleFor(x => x.NombreProducto)
            .NotEmpty().WithMessage("El nombre del producto es obligatorio")
            .MaximumLength(200).WithMessage("El nombre del producto no puede exceder 200 caracteres");

        RuleFor(x => x.Descripcion)
            .MaximumLength(500).WithMessage("La descripción no puede exceder 500 caracteres");

        RuleFor(x => x.Cantidad)
            .GreaterThan(0).WithMessage("La cantidad debe ser mayor a 0")
            .LessThanOrEqualTo(100).WithMessage("La cantidad no puede exceder 100");

        RuleFor(x => x.PrecioUnitario)
            .GreaterThan(0).WithMessage("El precio unitario debe ser mayor a 0")
            .LessThanOrEqualTo(999999.99m).WithMessage("El precio unitario no puede exceder 999,999.99");
    }
}

public class CrearDireccionEntregaRequestValidator : AbstractValidator<CrearDireccionEntregaRequest>
{
    public CrearDireccionEntregaRequestValidator()
    {
        RuleFor(x => x.Calle)
            .NotEmpty().WithMessage("La calle es obligatoria")
            .MaximumLength(200).WithMessage("La calle no puede exceder 200 caracteres");

        RuleFor(x => x.Numero)
            .MaximumLength(100).WithMessage("El número no puede exceder 100 caracteres");

        RuleFor(x => x.Interior)
            .MaximumLength(100).WithMessage("El interior no puede exceder 100 caracteres");

        RuleFor(x => x.Referencia)
            .MaximumLength(100).WithMessage("La referencia no puede exceder 100 caracteres");

        RuleFor(x => x.Ciudad)
            .NotEmpty().WithMessage("La ciudad es obligatoria")
            .MaximumLength(100).WithMessage("La ciudad no puede exceder 100 caracteres");

        RuleFor(x => x.Departamento)
            .MaximumLength(100).WithMessage("El departamento no puede exceder 100 caracteres");

        RuleFor(x => x.CodigoPostal)
            .MaximumLength(20).WithMessage("El código postal no puede exceder 20 caracteres");

        RuleFor(x => x.Latitud)
            .NotNull().WithMessage("La latitud es obligatoria")
            .InclusiveBetween(-90, 90).WithMessage("La latitud debe estar entre -90 y 90");

        RuleFor(x => x.Longitud)
            .NotNull().WithMessage("La longitud es obligatoria")
            .InclusiveBetween(-180, 180).WithMessage("La longitud debe estar entre -180 y 180");
    }
}

public class CrearContactoPedidoRequestValidator : AbstractValidator<CrearContactoPedidoRequest>
{
    public CrearContactoPedidoRequestValidator()
    {
        RuleFor(x => x.Tipo)
            .NotEmpty().WithMessage("El tipo de contacto es obligatorio")
            .MaximumLength(50).WithMessage("El tipo no puede exceder 50 caracteres")
            .Must(tipo => new[] { "Cliente", "Restaurante", "Repartidor" }.Contains(tipo))
            .WithMessage("El tipo debe ser: Cliente, Restaurante o Repartidor");

        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre es obligatorio")
            .MaximumLength(100).WithMessage("El nombre no puede exceder 100 caracteres");

        RuleFor(x => x.Telefono)
            .NotEmpty().WithMessage("El teléfono es obligatorio")
            .MaximumLength(20).WithMessage("El teléfono no puede exceder 20 caracteres")
            .Matches(@"^\+?[\d\s\-\(\)]+$").WithMessage("El formato del teléfono no es válido");

        RuleFor(x => x.Email)
            .MaximumLength(256).WithMessage("El email no puede exceder 256 caracteres")
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage("El formato del email no es válido");
    }
}

public class CrearPedidoRequestValidator : AbstractValidator<CrearPedidoRequest>
{
    public CrearPedidoRequestValidator()
    {
        RuleFor(x => x.RestauranteId)
            .NotEmpty().WithMessage("El ID del restaurante es obligatorio");

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("El pedido debe tener al menos un item")
            .Must(items => items.Count <= 50).WithMessage("El pedido no puede tener más de 50 items");

        RuleForEach(x => x.Items)
            .SetValidator(new CrearItemPedidoRequestValidator())
            .WithMessage("Item inválido");

        RuleFor(x => x.DireccionEntrega)
            .NotNull().WithMessage("La dirección de entrega es obligatoria")
            .SetValidator(new CrearDireccionEntregaRequestValidator());

        RuleFor(x => x.Contactos)
            .NotEmpty().WithMessage("El pedido debe tener al menos un contacto")
            .Must(contactos => contactos.Count <= 10).WithMessage("El pedido no puede tener más de 10 contactos");

        RuleForEach(x => x.Contactos)
            .SetValidator(new CrearContactoPedidoRequestValidator())
            .WithMessage("Contacto inválido");

        RuleFor(x => x.NotasCliente)
            .MaximumLength(500).WithMessage("Las notas del cliente no pueden exceder 500 caracteres");

        // Validación cruzada: al menos un contacto de tipo Cliente
        RuleFor(x => x.Contactos)
            .Must(contactos => contactos.Any(c => c.Tipo == "Cliente"))
            .WithMessage("Debe haber al menos un contacto de tipo Cliente")
            .When(x => x.Contactos != null);
    }
}