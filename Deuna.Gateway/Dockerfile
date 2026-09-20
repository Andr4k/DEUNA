# Deuna.Gateway/Dockerfile
# Multi-stage build para Gateway (YARP)

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
WORKDIR /app
EXPOSE 8080

# Usuario no-root para seguridad
RUN adduser -D -u 1001 appuser
USER appuser

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["Deuna.Gateway/Deuna.Gateway.csproj", "Deuna.Gateway/"]
COPY ["Deuna.Shared/Deuna.Shared.csproj", "Deuna.Shared/"]
RUN dotnet restore "Deuna.Gateway/Deuna.Gateway.csproj"

COPY . .
WORKDIR "/src/Deuna.Gateway"
RUN dotnet build "Deuna.Gateway.csproj" -c Release -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish "Deuna.Gateway.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Deuna.Gateway.dll"]