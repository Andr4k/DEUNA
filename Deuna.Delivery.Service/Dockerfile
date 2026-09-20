# Deuna.Delivery.Service/Dockerfile
# Multi-stage build para Delivery Service

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
WORKDIR /app
EXPOSE 8080

# Usuario no-root para seguridad
RUN adduser -D -u 1001 appuser
USER appuser

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["Deuna.Delivery.Service/Deuna.Delivery.Service.csproj", "Deuna.Delivery.Service/"]
COPY ["Deuna.Shared/Deuna.Shared.csproj", "Deuna.Shared/"]
RUN dotnet restore "Deuna.Delivery.Service/Deuna.Delivery.Service.csproj"

COPY . .
WORKDIR "/src/Deuna.Delivery.Service"
RUN dotnet build "Deuna.Delivery.Service.csproj" -c Release -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish "Deuna.Delivery.Service.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Deuna.Delivery.Service.dll"]