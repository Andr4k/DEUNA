# Deuna.Notification.Service/Dockerfile
# Multi-stage build para Notification Service

FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copiar solo los csproj primero para cache de capas
COPY ["Deuna.Notification.Service/Deuna.Notification.Service.csproj", "Deuna.Notification.Service/"]
COPY ["Deuna.Shared/Deuna.Shared.csproj", "Deuna.Shared/"]

# Restaurar dependencias
WORKDIR "/src/Deuna.Notification.Service"
RUN dotnet restore "Deuna.Notification.Service.csproj"

# Copiar SOLO el código fuente necesario
# Destino absoluto: evita anidar el proyecto dentro de sí mismo (WORKDIR es /src/Deuna.Notification.Service)
COPY Deuna.Notification.Service/ /src/Deuna.Notification.Service/
COPY Deuna.Shared/ /src/Deuna.Shared/

# Build y publish
RUN dotnet publish "Deuna.Notification.Service.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app

# Usuario no-root para seguridad
RUN adduser -D -u 1001 appuser
USER appuser

COPY --from=build --chown=appuser:appuser /app/publish .
ENTRYPOINT ["dotnet", "Deuna.Notification.Service.dll"]
