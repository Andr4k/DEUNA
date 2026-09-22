# Deuna.Feedback.Service/Dockerfile
# Multi-stage build para Feedback Service

FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copiar solo los csproj primero para cache de capas
COPY ["Deuna.Feedback.Service/Deuna.Feedback.Service.csproj", "Deuna.Feedback.Service/"]
COPY ["Deuna.Shared/Deuna.Shared.csproj", "Deuna.Shared/"]

# Restaurar dependencias
WORKDIR "/src/Deuna.Feedback.Service"
RUN dotnet restore "Deuna.Feedback.Service.csproj"

# Copiar SOLO el código fuente necesario
COPY Deuna.Feedback.Service/ Deuna.Feedback.Service/
COPY Deuna.Shared/ Deuna.Shared/

# Build y publish
RUN dotnet publish "Deuna.Feedback.Service.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app

# Usuario no-root para seguridad
RUN adduser -D -u 1001 appuser
USER appuser

COPY --from=build --chown=appuser:appuser /app/publish .
ENTRYPOINT ["dotnet", "Deuna.Feedback.Service.dll"]