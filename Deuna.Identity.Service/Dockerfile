# Deuna.Identity.Service/Dockerfile
# Multi-stage build para Identity Service

FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copiar solo los csproj primero para cache de capas
COPY ["Deuna.Identity.Service/Deuna.Identity.Service.csproj", "Deuna.Identity.Service/"]
COPY ["Deuna.Shared/Deuna.Shared.csproj", "Deuna.Shared/"]

# Restaurar dependencias
WORKDIR "/src/Deuna.Identity.Service"
RUN dotnet restore "Deuna.Identity.Service.csproj"

# Copiar SOLO el código fuente necesario (excluye otros servicios)
# Destino absoluto: evita anidar el proyecto dentro de sí mismo (WORKDIR es /src/Deuna.Identity.Service)
COPY Deuna.Identity.Service/ /src/Deuna.Identity.Service/
COPY Deuna.Shared/ /src/Deuna.Shared/

# Build y publish en un solo paso para evitar problemas de permisos
RUN dotnet publish "Deuna.Identity.Service.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app

# Usuario no-root para seguridad
RUN adduser -D -u 1001 appuser
USER appuser

COPY --from=build --chown=appuser:appuser /app/publish .
ENTRYPOINT ["dotnet", "Deuna.Identity.Service.dll"]