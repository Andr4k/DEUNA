using System.ComponentModel.DataAnnotations;

namespace Deuna.Identity.Service.DTOs;

public record RegisterRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(8), MaxLength(100)] string Password,
    [MaxLength(100)] string? FirstName,
    [MaxLength(100)] string? LastName,
    [MaxLength(20), Phone] string? PhoneNumber,
    [Required] string Role // "Restaurant", "Courier", "Admin"
);

public record RegisterResponse(
    Guid UserId,
    string Email,
    string Role,
    string Message
);

public record LoginRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required] string Password,
    bool RememberMe = false
);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    Guid UserId,
    string Email,
    string Role,
    string FirstName,
    string LastName
);

public record RefreshTokenRequest(
    [Required] string RefreshToken
);

public record RefreshTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt
);

public record LogoutRequest(
    [Required] string RefreshToken
);

public record ForgotPasswordRequest(
    [Required, EmailAddress, MaxLength(256)] string Email
);

public record ResetPasswordRequest(
    [Required] string Token,
    [Required, MinLength(8), MaxLength(100)] string NewPassword,
    [Required] string ConfirmPassword
);

public record VerifyEmailRequest(
    [Required] string Token
);

public record VerifyPhoneRequest(
    [Required] string Code,
    [Required, Phone, MaxLength(20)] string PhoneNumber
);

public record SendVerificationCodeRequest(
    [Required, EmailAddress, MaxLength(256)] string Email
);

public record SendPhoneCodeRequest(
    [Required, Phone, MaxLength(20)] string PhoneNumber
);

public record AuthResponse(
    bool Success,
    string Message,
    object? Data = null
);