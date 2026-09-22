# Deuna.Orders.Service/Dockerfile
# Multi-stage build para Orders Service

FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copiar solo los csproj primero para cache de capas
COPY ["Deuna.Orders.Service/Deuna.Orders.Service.csproj", "Deuna.Orders.Service/"]
COPY ["Deuna.Shared/Deuna.Shared.csproj", "Deuna.Shared/"]

# Restaurar dependencias
WORKDIR "/src/Deuna.Orders.Service"
RUN dotnet restore "Deuna.Orders.Service.csproj"

# Copiar SOLO el código fuente necesario
COPY Deuna.Orders.Service/ Deuna.Orders.Service/
COPY Deuna.Shared/ Deuna.Shared/

# Build y publish
RUN dotnet publish "Deuna.Orders.Service.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app

# Usuario no-root para seguridad
RUN adduser -D -u 1001 appuser
USER appuser

COPY --from=build --chown=appuser:appuser /app/publish .
ENTRYPOINT ["dotnet", "Deuna.Orders.Service.dll"]