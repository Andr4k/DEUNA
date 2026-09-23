using FluentValidation;
using Deuna.Identity.Service.DTOs;

namespace Deuna.Identity.Service.Validators;

public class RegisterRestaurantRequestValidator : AbstractValidator<RegisterRestaurantRequest>
{
    public RegisterRestaurantRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email es requerido")
            .EmailAddress().WithMessage("Email inválido")
            .MaximumLength(256).WithMessage("Email demasiado largo");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Contraseña es requerida")
            .MinimumLength(8).WithMessage("Contraseña debe tener al menos 8 caracteres")
            .MaximumLength(100).WithMessage("Contraseña demasiado larga")
            .Matches("[A-Z]").WithMessage("Contraseña debe tener al menos una mayúscula")
            .Matches("[a-z]").WithMessage("Contraseña debe tener al menos una minúscula")
            .Matches("[0-9]").WithMessage("Contraseña debe tener al menos un número")
            .Matches("[^a-zA-Z0-9]").WithMessage("Contraseña debe tener al menos un carácter especial");

        RuleFor(x => x.FirstName)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.FirstName))
            .WithMessage("Nombre demasiado largo");

        RuleFor(x => x.LastName)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.LastName))
            .WithMessage("Apellido demasiado largo");

        RuleFor(x => x.PhoneNumber)
            .Matches(@"^\+?[1-9]\d{1,14}$").When(x => !string.IsNullOrEmpty(x.PhoneNumber))
            .WithMessage("Número de teléfono inválido");

        RuleFor(x => x.RazonSocial)
            .NotEmpty().WithMessage("Razón social es requerida")
            .MaximumLength(255).WithMessage("Razón social demasiado larga");

        RuleFor(x => x.NombreComercial)
            .NotEmpty().WithMessage("Nombre comercial es requerido")
            .MaximumLength(255).WithMessage("Nombre comercial demasiado largo");

        RuleFor(x => x.Nit)
            .NotEmpty().WithMessage("NIT es requerido")
            .MaximumLength(50).WithMessage("NIT demasiado largo");

        RuleFor(x => x.DireccionSede)
            .NotEmpty().WithMessage("Dirección de sede es requerida")
            .MaximumLength(500).WithMessage("Dirección de sede demasiado larga");

        RuleFor(x => x.Ciudad)
            .NotEmpty().WithMessage("Ciudad es requerida")
            .MaximumLength(100).WithMessage("Ciudad demasiado larga");

        RuleFor(x => x.Latitud)
            .NotEmpty().WithMessage("Latitud es requerida")
            .InclusiveBetween(-90, 90).WithMessage("Latitud inválida");

        RuleFor(x => x.Longitud)
            .NotEmpty().WithMessage("Longitud es requerida")
            .InclusiveBetween(-180, 180).WithMessage("Longitud inválida");

        // Sin código de autorización validado no hay registro: la continuidad se prueba
        // con el token que devuelve validate-code (FR-001.8 a FR-001.10).
        RuleFor(x => x.ContinuidadToken)
            .NotEmpty().WithMessage("El código de autorización es obligatorio para registrarse")
            .MaximumLength(2048).WithMessage("Token de continuidad demasiado largo");
    }
}

public class EmitAuthorizationCodeRequestValidator : AbstractValidator<EmitAuthorizationCodeRequest>
{
    public EmitAuthorizationCodeRequestValidator()
    {
        RuleFor(x => x.Notas)
            .MaximumLength(200).WithMessage("Las notas no pueden superar los 200 caracteres");

        RuleFor(x => x.VigenciaDias)
            .InclusiveBetween(1, 365).When(x => x.VigenciaDias.HasValue)
            .WithMessage("La vigencia debe estar entre 1 y 365 días");
    }
}

public class ValidateAuthorizationCodeRequestValidator : AbstractValidator<ValidateAuthorizationCodeRequest>
{
    public ValidateAuthorizationCodeRequestValidator()
    {
        RuleFor(x => x.Codigo)
            .NotEmpty().WithMessage("El código de autorización es obligatorio")
            .MaximumLength(32).WithMessage("Código de autorización inválido");
    }
}

public class RegisterRiderRequestValidator : AbstractValidator<RegisterRiderRequest>
{
    public RegisterRiderRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email es requerido")
            .EmailAddress().WithMessage("Email inválido")
            .MaximumLength(256).WithMessage("Email demasiado largo");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Contraseña es requerida")
            .MinimumLength(8).WithMessage("Contraseña debe tener al menos 8 caracteres")
            .MaximumLength(100).WithMessage("Contraseña demasiado larga")
            .Matches("[A-Z]").WithMessage("Contraseña debe tener al menos una mayúscula")
            .Matches("[a-z]").WithMessage("Contraseña debe tener al menos una minúscula")
            .Matches("[0-9]").WithMessage("Contraseña debe tener al menos un número")
            .Matches("[^a-zA-Z0-9]").WithMessage("Contraseña debe tener al menos un carácter especial");

        RuleFor(x => x.FirstName)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.FirstName))
            .WithMessage("Nombre demasiado largo");

        RuleFor(x => x.LastName)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.LastName))
            .WithMessage("Apellido demasiado largo");

        RuleFor(x => x.PhoneNumber)
            .Matches(@"^\+?[1-9]\d{1,14}$").When(x => !string.IsNullOrEmpty(x.PhoneNumber))
            .WithMessage("Número de teléfono inválido");

        RuleFor(x => x.NombreCompleto)
            .NotEmpty().WithMessage("Nombre completo es requerido")
            .MaximumLength(255).WithMessage("Nombre completo demasiado largo");

        RuleFor(x => x.DocumentoIdentidad)
            .NotEmpty().WithMessage("Documento de identidad es requerido")
            .MaximumLength(50).WithMessage("Documento de identidad demasiado largo");

        RuleFor(x => x.CiudadOperacion)
            .NotEmpty().WithMessage("Ciudad de operación es requerida")
            .MaximumLength(100).WithMessage("Ciudad de operación demasiado larga");

        RuleFor(x => x.FotoPerfilUrl)
            .MaximumLength(500).When(x => !string.IsNullOrEmpty(x.FotoPerfilUrl))
            .WithMessage("URL de foto de perfil demasiado larga");
    }
}

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email es requerido")
            .EmailAddress().WithMessage("Email inválido")
            .MaximumLength(256).WithMessage("Email demasiado largo");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Contraseña es requerida")
            .MinimumLength(8).WithMessage("Contraseña debe tener al menos 8 caracteres")
            .MaximumLength(100).WithMessage("Contraseña demasiado larga")
            .Matches("[A-Z]").WithMessage("Contraseña debe tener al menos una mayúscula")
            .Matches("[a-z]").WithMessage("Contraseña debe tener al menos una minúscula")
            .Matches("[0-9]").WithMessage("Contraseña debe tener al menos un número")
            .Matches("[^a-zA-Z0-9]").WithMessage("Contraseña debe tener al menos un carácter especial");

        RuleFor(x => x.FirstName)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.FirstName))
            .WithMessage("Nombre demasiado largo");

        RuleFor(x => x.LastName)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.LastName))
            .WithMessage("Apellido demasiado largo");

        RuleFor(x => x.PhoneNumber)
            .Matches(@"^\+?[1-9]\d{1,14}$").When(x => !string.IsNullOrEmpty(x.PhoneNumber))
            .WithMessage("Número de teléfono inválido");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Rol es requerido")
            .Must(r => new[] { "Restaurant", "Courier", "Admin" }.Contains(r))
            .WithMessage("Rol debe ser: Restaurant, Courier o Admin");
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email es requerido")
            .EmailAddress().WithMessage("Email inválido");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Contraseña es requerida");
    }
}

public class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token es requerido");
    }
}

public class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token es requerido");
    }
}

public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email es requerido")
            .EmailAddress().WithMessage("Email inválido");
    }
}

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token es requerido");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Nueva contraseña es requerida")
            .MinimumLength(8).WithMessage("Contraseña debe tener al menos 8 caracteres")
            .MaximumLength(100).WithMessage("Contraseña demasiado larga")
            .Matches("[A-Z]").WithMessage("Contraseña debe tener al menos una mayúscula")
            .Matches("[a-z]").WithMessage("Contraseña debe tener al menos una minúscula")
            .Matches("[0-9]").WithMessage("Contraseña debe tener al menos un número")
            .Matches("[^a-zA-Z0-9]").WithMessage("Contraseña debe tener al menos un carácter especial");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword).WithMessage("Las contraseñas no coinciden");
    }
}

public class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token es requerido");
    }
}

public class VerifyPhoneRequestValidator : AbstractValidator<VerifyPhoneRequest>
{
    public VerifyPhoneRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Código es requerido")
            .Length(6).WithMessage("Código debe tener 6 dígitos")
            .Matches(@"^\d{6}$").WithMessage("Código debe ser numérico");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Teléfono es requerido")
            .Matches(@"^\+?[1-9]\d{1,14}$").WithMessage("Número de teléfono inválido");
    }
}

public class SendVerificationCodeRequestValidator : AbstractValidator<SendVerificationCodeRequest>
{
    public SendVerificationCodeRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email es requerido")
            .EmailAddress().WithMessage("Email inválido");
    }
}

public class SendPhoneCodeRequestValidator : AbstractValidator<SendPhoneCodeRequest>
{
    public SendPhoneCodeRequestValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Teléfono es requerido")
            .Matches(@"^\+?[1-9]\d{1,14}$").WithMessage("Número de teléfono inválido");
    }
}