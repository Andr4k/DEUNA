using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Deuna.Identity.Service.DTOs;
using Deuna.Identity.Service.Models;
using Deuna.Identity.Service.Models.Domain;
using Deuna.Shared.Extensions;
using Deuna.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Deuna.Identity.Service.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterRestaurantAsync(RegisterRestaurantRequest request);
    Task<AuthResponse> RegisterRiderAsync(RegisterRiderRequest request);
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request);
    Task<AuthResponse> LogoutAsync(LogoutRequest request);
    Task<AuthResponse> ForgotPasswordAsync(ForgotPasswordRequest request);
    Task<AuthResponse> ResetPasswordAsync(ResetPasswordRequest request);
    Task<AuthResponse> VerifyEmailAsync(VerifyEmailRequest request);
    Task<AuthResponse> SendVerificationEmailAsync(SendVerificationCodeRequest request);
    Task<AuthResponse> VerifyPhoneAsync(VerifyPhoneRequest request);
    Task<AuthResponse> SendPhoneCodeAsync(SendPhoneCodeRequest request);
}

public class AuthService : IAuthService
{
    private readonly IdentityDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IdentityDbContext db, IConfiguration config, ILogger<AuthService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterRestaurantAsync(RegisterRestaurantRequest request)
    {
        var existingUser = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (existingUser != null)
        {
            return new AuthResponse(false, "El email ya está registrado");
        }

        if (request.PhoneNumber != null)
        {
            var existingPhone = await _db.Usuarios.FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber);
            if (existingPhone != null)
            {
                return new AuthResponse(false, "El número de teléfono ya está registrado");
            }
        }

        var existingNit = await _db.PerfilesRestaurante.FirstOrDefaultAsync(p => p.Nit == request.Nit);
        if (existingNit != null)
        {
            return new AuthResponse(false, "El NIT ya está registrado");
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

        var user = new Usuario
        {
            Email = request.Email,
            PasswordHash = passwordHash,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            IsActive = true,
            EmailConfirmed = false,
            PhoneConfirmed = false
        };

        _db.Usuarios.Add(user);
        await _db.SaveChangesAsync();

        _db.PerfilesRestaurante.Add(new PerfilRestaurante
        {
            UsuarioId = user.Id,
            RazonSocial = request.RazonSocial,
            NombreComercial = request.NombreComercial,
            Nit = request.Nit,
            Activo = true,
            AceptaPedidos = false
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Restaurante registrado: {Email}", request.Email);

        return new AuthResponse(true, "Registro exitoso. Verifique su email.", new
        {
            UserId = user.Id,
            Email = user.Email,
            Role = Roles.Restaurant,
            VerificationToken = GenerateSecureToken()
        });
    }

    public async Task<AuthResponse> RegisterRiderAsync(RegisterRiderRequest request)
    {
        var existingUser = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (existingUser != null)
        {
            return new AuthResponse(false, "El email ya está registrado");
        }

        if (request.PhoneNumber != null)
        {
            var existingPhone = await _db.Usuarios.FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber);
            if (existingPhone != null)
            {
                return new AuthResponse(false, "El número de teléfono ya está registrado");
            }
        }

        var existingDoc = await _db.PerfilesRepartidor.FirstOrDefaultAsync(p => p.NumeroLicencia == request.DocumentoIdentidad);
        if (existingDoc != null)
        {
            return new AuthResponse(false, "El documento de identidad ya está registrado");
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

        var user = new Usuario
        {
            Email = request.Email,
            PasswordHash = passwordHash,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            IsActive = true,
            EmailConfirmed = false,
            PhoneConfirmed = false
        };

        _db.Usuarios.Add(user);
        await _db.SaveChangesAsync();

        _db.PerfilesRepartidor.Add(new PerfilRepartidor
        {
            UsuarioId = user.Id,
            NumeroLicencia = request.DocumentoIdentidad,
            NombreCompleto = request.NombreCompleto,
            CiudadOperacion = request.CiudadOperacion,
            FotoPerfilUrl = request.FotoPerfilUrl,
            Activo = true,
            Disponible = false
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Repartidor registrado: {Email}", request.Email);

        return new AuthResponse(true, "Registro exitoso. Verifique su email.", new
        {
            UserId = user.Id,
            Email = user.Email,
            Role = Roles.Rider,
            VerificationToken = GenerateSecureToken()
        });
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var existingUser = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (existingUser != null)
        {
            return new AuthResponse(false, "El email ya está registrado");
        }

        if (request.PhoneNumber != null)
        {
            var existingPhone = await _db.Usuarios.FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber);
            if (existingPhone != null)
            {
                return new AuthResponse(false, "El número de teléfono ya está registrado");
            }
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

        var user = new Usuario
        {
            Email = request.Email,
            PasswordHash = passwordHash,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            IsActive = true,
            EmailConfirmed = false,
            PhoneConfirmed = false
        };

        _db.Usuarios.Add(user);
        await _db.SaveChangesAsync();

        // Create profile based on role (roles canónicos de Deuna.Shared.Security.Roles)
        switch (request.Role?.ToUpperInvariant())
        {
            case Roles.Restaurant:
                _db.PerfilesRestaurante.Add(new PerfilRestaurante
                {
                    UsuarioId = user.Id,
                    NombreComercial = $"{request.FirstName} {request.LastName}".Trim(),
                    Activo = true,
                    AceptaPedidos = false // Until profile is completed
                });
                break;
            case Roles.Rider:
            case "COURIER":
                _db.PerfilesRepartidor.Add(new PerfilRepartidor
                {
                    UsuarioId = user.Id,
                    Activo = true,
                    Disponible = false
                });
                break;
            case Roles.Admin:
                _db.PerfilesAdministrador.Add(new PerfilAdministrador
                {
                    UsuarioId = user.Id,
                    Cargo = "OPERADOR",
                    Activo = true,
                    EsSuperAdmin = false
                });
                break;
        }

        await _db.SaveChangesAsync();

        // Generate email verification token
        var verificationToken = GenerateSecureToken();
        var refreshToken = new RefreshToken
        {
            UsuarioId = user.Id,
            Token = verificationToken,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedByIp = "register"
        };
        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Usuario registrado: {Email} con rol {Role}", request.Email, request.Role);

        return new AuthResponse(true, "Registro exitoso. Verifique su email.", new
        {
            UserId = user.Id,
            Email = user.Email,
            Role = request.Role,
            VerificationToken = verificationToken // In production, send via email
        });
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _db.Usuarios
            .Include(u => u.PerfilRestaurante)
            .Include(u => u.PerfilRepartidor)
            .Include(u => u.PerfilAdministrador)
            .FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Intento de login fallido para: {Email}", request.Email);
            return new AuthResponse(false, "Credenciales inválidas");
        }

        if (!user.IsActive)
        {
            return new AuthResponse(false, "La cuenta está desactivada");
        }

        if (user.DeletedAt != null)
        {
            return new AuthResponse(false, "La cuenta ha sido eliminada");
        }

        // Get role
        var role = GetUserRole(user);
        if (role == Roles.Admin && user.PerfilAdministrador?.Activo != true)
        {
            return new AuthResponse(false, "Perfil de administrador inactivo");
        }

        // Generate tokens - JWT expires in 8 hours per spec
        var accessToken = GenerateAccessToken(user, role);
        var refreshTokenValue = GenerateSecureToken();
        var expiresAt = DateTime.UtcNow.AddHours(8); // 8 hours fixed per spec

        var refreshToken = new RefreshToken
        {
            UsuarioId = user.Id,
            Token = refreshTokenValue,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByIp = "login"
        };

        _db.RefreshTokens.Add(refreshToken);

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Login exitoso: {Email} ({Role})", request.Email, role);

        return new AuthResponse(true, "Login exitoso", new LoginResponse(
            accessToken,
            refreshTokenValue,
            expiresAt,
            user.Id,
            user.Email,
            role,
            user.FirstName ?? "",
            user.LastName ?? ""
        ));
    }

    public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request)
    {
        var storedToken = await _db.RefreshTokens
            .Include(rt => rt.Usuario)
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

        if (storedToken == null)
        {
            return new AuthResponse(false, "Refresh token inválido");
        }

        if (storedToken.IsRevoked)
        {
            return new AuthResponse(false, "Refresh token revocado");
        }

        if (storedToken.ExpiresAt < DateTime.UtcNow)
        {
            return new AuthResponse(false, "Refresh token expirado");
        }

        var user = storedToken.Usuario;
        if (user == null || !user.IsActive || user.DeletedAt != null)
        {
            return new AuthResponse(false, "Usuario inválido");
        }

        var role = GetUserRole(user);

        // Revoke old token
        storedToken.IsRevoked = true;
        storedToken.RevokedAt = DateTime.UtcNow;

        // Generate new tokens
        var newAccessToken = GenerateAccessToken(user, role);
        var newRefreshTokenValue = GenerateSecureToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(GetAccessTokenExpiryMinutes(false));

        var newRefreshToken = new RefreshToken
        {
            UsuarioId = user.Id,
            Token = newRefreshTokenValue,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByIp = "refresh",
            ReplacedByToken = request.RefreshToken
        };

        _db.RefreshTokens.Add(newRefreshToken);
        await _db.SaveChangesAsync();

        return new AuthResponse(true, "Token renovado", new RefreshTokenResponse(
            newAccessToken,
            newRefreshTokenValue,
            expiresAt
        ));
    }

    public async Task<AuthResponse> LogoutAsync(LogoutRequest request)
    {
        var storedToken = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

        if (storedToken != null && !storedToken.IsRevoked)
        {
            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return new AuthResponse(true, "Logout exitoso");
    }

    public async Task<AuthResponse> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        var user = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == request.Email);

        // Always return success to prevent email enumeration
        if (user == null)
        {
            return new AuthResponse(true, "Si el email existe, recibirá instrucciones para resetear la contraseña");
        }

        var resetToken = GenerateSecureToken();
        var refreshToken = new RefreshToken
        {
            UsuarioId = user.Id,
            Token = resetToken,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedByIp = "forgot_password"
        };

        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Solicitud de reset de contraseña para: {Email}", request.Email);

        // In production, send email with resetToken
        return new AuthResponse(true, "Si el email existe, recibirá instrucciones para resetear la contraseña", new
        {
            ResetToken = resetToken // Only for development
        });
    }

    public async Task<AuthResponse> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var storedToken = await _db.RefreshTokens
            .Include(rt => rt.Usuario)
            .FirstOrDefaultAsync(rt => rt.Token == request.Token);

        if (storedToken == null || storedToken.IsRevoked || storedToken.ExpiresAt < DateTime.UtcNow)
        {
            return new AuthResponse(false, "Token inválido o expirado");
        }

        var user = storedToken.Usuario;
        if (user == null)
        {
            return new AuthResponse(false, "Usuario no encontrado");
        }

        // Verify it's a password reset token (created by forgot_password)
        if (storedToken.CreatedByIp != "forgot_password")
        {
            return new AuthResponse(false, "Token inválido");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, workFactor: 12);
        user.UpdatedAt = DateTime.UtcNow;

        // Revoke the reset token
        storedToken.IsRevoked = true;
        storedToken.RevokedAt = DateTime.UtcNow;

        // Also revoke all existing refresh tokens for security
        var activeTokens = await _db.RefreshTokens
            .Where(rt => rt.UsuarioId == user.Id && !rt.IsRevoked)
            .ToListAsync();

        foreach (var token in activeTokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Contraseña reseteada para: {Email}", user.Email);

        return new AuthResponse(true, "Contraseña actualizada correctamente");
    }

    public async Task<AuthResponse> VerifyEmailAsync(VerifyEmailRequest request)
    {
        var storedToken = await _db.RefreshTokens
            .Include(rt => rt.Usuario)
            .FirstOrDefaultAsync(rt => rt.Token == request.Token);

        if (storedToken == null || storedToken.IsRevoked || storedToken.ExpiresAt < DateTime.UtcNow)
        {
            return new AuthResponse(false, "Token inválido o expirado");
        }

        if (storedToken.CreatedByIp != "register")
        {
            return new AuthResponse(false, "Token inválido");
        }

        var user = storedToken.Usuario;
        if (user == null)
        {
            return new AuthResponse(false, "Usuario no encontrado");
        }

        user.EmailConfirmed = true;
        user.UpdatedAt = DateTime.UtcNow;

        storedToken.IsRevoked = true;
        storedToken.RevokedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Email verificado para: {Email}", user.Email);

        return new AuthResponse(true, "Email verificado correctamente");
    }

    public async Task<AuthResponse> SendVerificationEmailAsync(SendVerificationCodeRequest request)
    {
        var user = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user == null)
        {
            return new AuthResponse(true, "Si el email existe, recibirá un código de verificación");
        }

        if (user.EmailConfirmed)
        {
            return new AuthResponse(false, "El email ya está verificado");
        }

        var verificationToken = GenerateSecureToken();
        var refreshToken = new RefreshToken
        {
            UsuarioId = user.Id,
            Token = verificationToken,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedByIp = "register"
        };

        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync();

        // In production, send email with verificationToken
        return new AuthResponse(true, "Código de verificación enviado", new
        {
            VerificationToken = verificationToken // Only for development
        });
    }

    public async Task<AuthResponse> VerifyPhoneAsync(VerifyPhoneRequest request)
    {
        var user = await _db.Usuarios.FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber);

        if (user == null)
        {
            return new AuthResponse(false, "Número de teléfono no registrado");
        }

        if (user.PhoneConfirmed)
        {
            return new AuthResponse(false, "El teléfono ya está verificado");
        }

        // In production, verify the code against a stored verification code
        // For now, accept any 6-digit code in development
        if (request.Code != "123456" && !IsDevelopment())
        {
            return new AuthResponse(false, "Código inválido");
        }

        user.PhoneConfirmed = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Teléfono verificado para: {Email}", user.Email);

        return new AuthResponse(true, "Teléfono verificado correctamente");
    }

    public async Task<AuthResponse> SendPhoneCodeAsync(SendPhoneCodeRequest request)
    {
        var user = await _db.Usuarios.FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber);

        if (user == null)
        {
            return new AuthResponse(true, "Si el número existe, recibirá un código de verificación");
        }

        if (user.PhoneConfirmed)
        {
            return new AuthResponse(false, "El teléfono ya está verificado");
        }

        // In production, send SMS with code
        return new AuthResponse(true, "Código de verificación enviado (123456 en desarrollo)");
    }

    private string GenerateAccessToken(Usuario user, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(GetJwtSecret()));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        if (!string.IsNullOrEmpty(user.FirstName))
            claims.Add(new Claim(ClaimTypes.GivenName, user.FirstName));
        if (!string.IsNullOrEmpty(user.LastName))
            claims.Add(new Claim(ClaimTypes.Surname, user.LastName));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "Deuna.Identity",
            audience: _config["Jwt:Audience"] ?? "Deuna.Api",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8), // 8 hours fixed per spec
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    private string GetJwtSecret()
    {
        return _config["Jwt:SecretKey"] ?? "DeunaSuperSecretKey2026!@#$%^&*()_+-=[]{}|;:,.<>?";
    }

    private int GetAccessTokenExpiryMinutes(bool rememberMe)
    {
        return rememberMe ? 1440 : 60; // 24 hours or 1 hour
    }

    private string GetUserRole(Usuario user)
    {
        if (user.PerfilAdministrador != null) return Roles.Admin;
        if (user.PerfilRestaurante != null) return Roles.Restaurant;
        if (user.PerfilRepartidor != null) return Roles.Rider;
        return Roles.Customer;
    }

    private bool IsDevelopment()
    {
        return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
    }
}