using Deuna.Identity.Service.DTOs;
using Deuna.Identity.Service.Services;
using Deuna.Identity.Service.Validators;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Deuna.Identity.Service.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/identity")
            .WithTags("Auth")
            .WithOpenApi();

        group.MapPost("/register/restaurant", RegisterRestaurantAsync)
            .WithName("RegisterRestaurant")
            .WithSummary("Registrar nuevo restaurante")
            .AllowAnonymous();

        group.MapPost("/register/rider", RegisterRiderAsync)
            .WithName("RegisterRider")
            .WithSummary("Registrar nuevo repartidor")
            .AllowAnonymous();

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithSummary("Iniciar sesión")
            .AllowAnonymous()
            .RequireRateLimiting("login");

        group.MapPost("/refresh", RefreshTokenAsync)
            .WithName("RefreshToken")
            .WithSummary("Renovar access token")
            .AllowAnonymous();

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .WithSummary("Cerrar sesión")
            .AllowAnonymous();

        group.MapPost("/forgot-password", ForgotPasswordAsync)
            .WithName("ForgotPassword")
            .WithSummary("Solicitar reset de contraseña")
            .AllowAnonymous();

        group.MapPost("/reset-password", ResetPasswordAsync)
            .WithName("ResetPassword")
            .WithSummary("Resetear contraseña con token")
            .AllowAnonymous();

        group.MapPost("/verify-email", VerifyEmailAsync)
            .WithName("VerifyEmail")
            .WithSummary("Verificar email con token")
            .AllowAnonymous();

        group.MapPost("/send-verification-email", SendVerificationEmailAsync)
            .WithName("SendVerificationEmail")
            .WithSummary("Reenviar email de verificación")
            .AllowAnonymous();

        group.MapPost("/verify-phone", VerifyPhoneAsync)
            .WithName("VerifyPhone")
            .WithSummary("Verificar teléfono con código")
            .AllowAnonymous();

        group.MapPost("/send-phone-code", SendPhoneCodeAsync)
            .WithName("SendPhoneCode")
            .WithSummary("Enviar código de verificación por SMS")
            .AllowAnonymous();

        return app;
    }

    private static async Task<IResult> RegisterRestaurantAsync(
        [FromBody] RegisterRestaurantRequest request,
        [FromServices] IAuthService authService,
        [FromServices] IValidator<RegisterRestaurantRequest> validator)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new AuthResponse(false, "Datos inválidos", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await authService.RegisterRestaurantAsync(request);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    private static async Task<IResult> RegisterRiderAsync(
        [FromBody] RegisterRiderRequest request,
        [FromServices] IAuthService authService,
        [FromServices] IValidator<RegisterRiderRequest> validator)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new AuthResponse(false, "Datos inválidos", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await authService.RegisterRiderAsync(request);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        [FromServices] IAuthService authService,
        [FromServices] IValidator<LoginRequest> validator)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new AuthResponse(false, "Datos inválidos", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await authService.LoginAsync(request);
        if (!result.Success)
        {
            // Return 403 for inactive account, 401 for invalid credentials
            return result.Message == "La cuenta está desactivada" 
                ? Results.Forbid() 
                : Results.Unauthorized();
        }
        return Results.Ok(result);
    }

    private static async Task<IResult> RefreshTokenAsync(
        [FromBody] RefreshTokenRequest request,
        [FromServices] IAuthService authService,
        [FromServices] IValidator<RefreshTokenRequest> validator)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new AuthResponse(false, "Datos inválidos", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await authService.RefreshTokenAsync(request);
        return result.Success ? Results.Ok(result) : Results.Unauthorized();
    }

    private static async Task<IResult> LogoutAsync(
        [FromBody] LogoutRequest request,
        [FromServices] IAuthService authService,
        [FromServices] IValidator<LogoutRequest> validator)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new AuthResponse(false, "Datos inválidos", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await authService.LogoutAsync(request);
        return Results.Ok(result);
    }

    private static async Task<IResult> ForgotPasswordAsync(
        [FromBody] ForgotPasswordRequest request,
        [FromServices] IAuthService authService,
        [FromServices] IValidator<ForgotPasswordRequest> validator)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new AuthResponse(false, "Datos inválidos", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await authService.ForgotPasswordAsync(request);
        return Results.Ok(result);
    }

    private static async Task<IResult> ResetPasswordAsync(
        [FromBody] ResetPasswordRequest request,
        [FromServices] IAuthService authService,
        [FromServices] IValidator<ResetPasswordRequest> validator)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new AuthResponse(false, "Datos inválidos", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await authService.ResetPasswordAsync(request);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    private static async Task<IResult> VerifyEmailAsync(
        [FromBody] VerifyEmailRequest request,
        [FromServices] IAuthService authService,
        [FromServices] IValidator<VerifyEmailRequest> validator)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new AuthResponse(false, "Datos inválidos", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await authService.VerifyEmailAsync(request);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    private static async Task<IResult> SendVerificationEmailAsync(
        [FromBody] SendVerificationCodeRequest request,
        [FromServices] IAuthService authService,
        [FromServices] IValidator<SendVerificationCodeRequest> validator)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new AuthResponse(false, "Datos inválidos", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await authService.SendVerificationEmailAsync(request);
        return Results.Ok(result);
    }

    private static async Task<IResult> VerifyPhoneAsync(
        [FromBody] VerifyPhoneRequest request,
        [FromServices] IAuthService authService,
        [FromServices] IValidator<VerifyPhoneRequest> validator)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new AuthResponse(false, "Datos inválidos", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await authService.VerifyPhoneAsync(request);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    private static async Task<IResult> SendPhoneCodeAsync(
        [FromBody] SendPhoneCodeRequest request,
        [FromServices] IAuthService authService,
        [FromServices] IValidator<SendPhoneCodeRequest> validator)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new AuthResponse(false, "Datos inválidos", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await authService.SendPhoneCodeAsync(request);
        return Results.Ok(result);
    }
}