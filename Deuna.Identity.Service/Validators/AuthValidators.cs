using FluentValidation;
using Deuna.Identity.Service.DTOs;

namespace Deuna.Identity.Service.Validators;

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