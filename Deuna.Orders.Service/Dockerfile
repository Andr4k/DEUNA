# Deuna.Orders.Service/Dockerfile
# Multi-stage build para Orders Service

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
WORKDIR /app
EXPOSE 8080

# Usuario no-root para seguridad
RUN adduser -D -u 1001 appuser
USER appuser

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["Deuna.Orders.Service/Deuna.Orders.Service.csproj", "Deuna.Orders.Service/"]
COPY ["Deuna.Shared/Deuna.Shared.csproj", "Deuna.Shared/"]
RUN dotnet restore "Deuna.Orders.Service/Deuna.Orders.Service.csproj"

COPY . .
WORKDIR "/src/Deuna.Orders.Service"
RUN dotnet build "Deuna.Orders.Service.csproj" -c Release -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish "Deuna.Orders.Service.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Deuna.Orders.Service.dll"]