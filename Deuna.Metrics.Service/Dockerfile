# Deuna.Metrics.Service/Dockerfile
# Multi-stage build para Metrics Service

FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copiar solo los csproj primero para cache de capas
COPY ["Deuna.Metrics.Service/Deuna.Metrics.Service.csproj", "Deuna.Metrics.Service/"]
COPY ["Deuna.Shared/Deuna.Shared.csproj", "Deuna.Shared/"]

# Restaurar dependencias
WORKDIR "/src/Deuna.Metrics.Service"
RUN dotnet restore "Deuna.Metrics.Service.csproj"

# Copiar SOLO el código fuente necesario
COPY Deuna.Metrics.Service/ /src/Deuna.Metrics.Service/
COPY Deuna.Shared/ /src/Deuna.Shared/

# Build y publish
RUN dotnet publish "Deuna.Metrics.Service.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app

# `tzdata` no viene en alpine y sin él no existen las zonas horarias: el servicio
# no puede resolver "hoy" en America/Bogota y las métricas del día quedarían
# corridas. Se instala acá en lugar de usar un offset fijo, que se rompe el día que
# se opere en un país con horario de verano.
RUN apk add --no-cache tzdata

# Usuario no-root para seguridad
RUN adduser -D -u 1001 appuser
USER appuser

COPY --from=build --chown=appuser:appuser /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "Deuna.Metrics.Service.dll"]
