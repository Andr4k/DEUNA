# Deuna.Identity.Service/Dockerfile
# Multi-stage build para Identity Service

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
WORKDIR /app
EXPOSE 8080

# Usuario no-root para seguridad
RUN adduser -D -u 1001 appuser
USER appuser

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["Deuna.Identity.Service/Deuna.Identity.Service.csproj", "Deuna.Identity.Service/"]
COPY ["Deuna.Shared/Deuna.Shared.csproj", "Deuna.Shared/"]
RUN dotnet restore "Deuna.Identity.Service/Deuna.Identity.Service.csproj"

COPY . .
WORKDIR "/src/Deuna.Identity.Service"
RUN dotnet build "Deuna.Identity.Service.csproj" -c Release -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish "Deuna.Identity.Service.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Deuna.Identity.Service.dll"]