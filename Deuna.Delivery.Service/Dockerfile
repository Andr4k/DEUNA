# Deuna.Delivery.Service/Dockerfile
# Multi-stage build para Delivery Service

FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copiar solo los csproj primero para cache de capas
COPY ["Deuna.Delivery.Service/Deuna.Delivery.Service.csproj", "Deuna.Delivery.Service/"]
COPY ["Deuna.Shared/Deuna.Shared.csproj", "Deuna.Shared/"]

# Restaurar dependencias
WORKDIR "/src/Deuna.Delivery.Service"
RUN dotnet restore "Deuna.Delivery.Service.csproj"

# Copiar SOLO el código fuente necesario
COPY Deuna.Delivery.Service/ Deuna.Delivery.Service/
COPY Deuna.Shared/ Deuna.Shared/

# Build y publish
RUN dotnet publish "Deuna.Delivery.Service.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app

# Usuario no-root para seguridad
RUN adduser -D -u 1001 appuser
USER appuser

COPY --from=build --chown=appuser:appuser /app/publish .
ENTRYPOINT ["dotnet", "Deuna.Delivery.Service.dll"]