<# 
.SYNOPSIS
    Inicia todos los microservicios DEUNA en modo desarrollo (Windows PowerShell)
.DESCRIPTION
    Levanta infraestructura con Docker Compose, compila la solución, aplica migraciones 
    e inicia todos los microservicios .NET en procesos separados.
.NOTES
    Requiere: Docker Desktop, .NET 10 SDK, PowerShell 7+
    Ejecutar desde la raíz del repositorio: .\start-dev.ps1
#>

param(
    [switch]$SkipBuild,
    [switch]$SkipMigrations,
    [switch]$DockerOnly
)

$ErrorActionPreference = "Stop"

# Colores
$Red = "`e[31m"
$Green = "`e[32m"
$Yellow = "`e[33m"
$Blue = "`e[34m"
$Reset = "`e[0m"

$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ComposeFile = Join-Path $RootDir "docker-compose.yml"
$EnvFile = Join-Path $RootDir ".env"
$LogsDir = Join-Path $RootDir "logs"

function Write-Color($Message, $Color) {
    Write-Host "$Color$Message$Reset"
}

function Check-Command($Command) {
    try {
        Get-Command $Command -ErrorAction Stop | Out-Null
        return $true
    } catch {
        return $false
    }
}

Write-Color "╔══════════════════════════════════════════════════════════════╗" $Blue
Write-Color "║           DEUNA - Microservicios Development Stack           ║" $Blue
Write-Color "╚══════════════════════════════════════════════════════════════╝" $Blue
Write-Host ""

# Verificar prerrequisitos
$missing = @()
if (-not (Check-Command "docker")) { $missing += "docker" }
if (-not (Check-Command "docker-compose")) { $missing += "docker-compose" }
if (-not (Check-Command "dotnet")) { $missing += "dotnet" }

if ($missing.Count -gt 0) {
    Write-Color "❌ Faltan prerrequisitos: $($missing -join ', ')" $Red
    exit 1
}

# Verificar .env
if (-not (Test-Path $EnvFile)) {
    Write-Color "⚠️  No existe .env, creando uno por defecto..." $Yellow
    @"
POSTGRES_PASSWORD=deuna_dev_2026
REDIS_PASSWORD=redis_dev_2026
RABBITMQ_USER=deuna
RABBITMQ_PASSWORD=rabbitmq_dev_2026
"@ | Set-Content $EnvFile -Encoding UTF8
    Write-Color "✅ .env creado con valores por defecto" $Green
}

# Cargar variables de entorno
$envVars = Get-Content $EnvFile | Where-Object { $_ -match '^[A-Z_]+=' }
foreach ($line in $envVars) {
    $parts = $line.Split('=', 2)
    if ($parts.Count -eq 2) {
        [Environment]::SetEnvironmentVariable($parts[0], $parts[1], "Process")
    }
}

# Cleanup al salir
$global:ServicePids = @()
function Cleanup {
    Write-Color "`n🛑 Deteniendo servicios..." $Yellow
    foreach ($pid in $global:ServicePids) {
        try { Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue } catch {}
    }
    if ($global:TailJob) { Stop-Job $global:TailJob -Force }
    Write-Color "✅ Servicios detenidos" $Green
    exit 0
}

# Registrar trap para Ctrl+C
[System.Console]::CancelKeyPress.Add({
    Cleanup
})

# ========== PASO 1: Infraestructura ==========
if (-not $DockerOnly) {
    Write-Color "📦 [1/4] Iniciando infraestructura (PostgreSQL, RabbitMQ, Redis)..." $Blue
    docker-compose -f $ComposeFile up -d postgres-identity postgres-orders postgres-delivery postgres-feedback rabbitmq redis

    Write-Color "⏳ Esperando healthchecks de infraestructura..." $Yellow
    
    # Esperar PostgreSQL Identity
    for ($i = 1; $i -le 30; $i++) {
        $status = docker-compose -f $ComposeFile ps postgres-identity | Select-String "healthy"
        if ($status) {
            Write-Color "✅ PostgreSQL Identity listo" $Green
            break
        }
        Start-Sleep 2
        if ($i -eq 30) { Write-Color "❌ Timeout PostgreSQL Identity" $Red; exit 1 }
    }

    # Esperar RabbitMQ
    for ($i = 1; $i -le 30; $i++) {
        $status = docker-compose -f $ComposeFile ps rabbitmq | Select-String "healthy"
        if ($status) {
            Write-Color "✅ RabbitMQ listo" $Green
            break
        }
        Start-Sleep 2
        if ($i -eq 30) { Write-Color "❌ Timeout RabbitMQ" $Red; exit 1 }
    }

    Write-Color "✅ Infraestructura lista" $Green
    Write-Host ""
}

# ========== PASO 2: Build ==========
if (-not $SkipBuild -and -not $DockerOnly) {
    Write-Color "🔨 [2/4] Compilando solución completa..." $Blue
    Push-Location $RootDir
    $buildResult = dotnet build Deuna.sln --no-incremental --verbosity quiet
    Pop-Location
    if ($LASTEXITCODE -eq 0) {
        Write-Color "✅ Build exitoso (0 errores)" $Green
    } else {
        Write-Color "❌ Error en build" $Red
        exit 1
    }
    Write-Host ""
}

# ========== PASO 3: Migraciones ==========
if (-not $SkipMigrations -and -not $DockerOnly) {
    Write-Color "🗄️  [3/4] Aplicando migraciones de base de datos..." $Blue
    
    # Identity
    Push-Location (Join-Path $RootDir "Deuna.Identity.Service")
    $result = dotnet ef database update --no-build 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Color "✅ Migraciones Identity aplicadas" $Green
    } else {
        Write-Color "⚠️  Migraciones Identity: ya aplicadas o error" $Yellow
    }
    Pop-Location
    Write-Host ""
}

# ========== PASO 4: Microservicios ==========
if (-not $DockerOnly) {
    Write-Color "🚀 [4/4] Iniciando microservicios..." $Blue

    # Crear directorio de logs
    if (-not (Test-Path $LogsDir)) { New-Item -ItemType Directory -Path $LogsDir | Out-Null }

    $services = @(
        @{ Name = "Gateway"; Path = "Deuna.Gateway"; Port = 5000; Log = "gateway.log" },
        @{ Name = "Identity"; Path = "Deuna.Identity.Service"; Port = 5001; Log = "identity.log" },
        @{ Name = "Orders"; Path = "Deuna.Orders.Service"; Port = 5002; Log = "orders.log" },
        @{ Name = "Delivery"; Path = "Deuna.Delivery.Service"; Port = 5003; Log = "delivery.log" },
        @{ Name = "Feedback"; Path = "Deuna.Feedback.Service"; Port = 5004; Log = "feedback.log" }
    )

    foreach ($svc in $services) {
        Write-Color "  ▶ $($svc.Name) (puerto $($svc.Port))..." $Yellow
        $svcPath = Join-Path $RootDir $svc.Path
        $logPath = Join-Path $LogsDir $svc.Log
        
        $job = Start-Job -ScriptBlock {
            param($Path, $Port, $LogPath, $EnvVars)
            foreach ($kv in $EnvVars.GetEnumerator()) {
                [Environment]::SetEnvironmentVariable($kv.Key, $kv.Value, "Process")
            }
            Set-Location $Path
            $env:ASPNETCORE_ENVIRONMENT = "Development"
            $env:ASPNETCORE_URLS = "http://localhost:$Port"
            dotnet run --no-build --urls "http://localhost:$Port" *>&1 | Tee-Object -FilePath $LogPath
        } -ArgumentList $svcPath, $svc.Port, $logPath, (Get-ChildItem Env: | Where-Object { $_.Name -match '^(POSTGRES|REDIS|RABBITMQ|JWT|YARP)' } | ForEach-Object { @{$_.Name = $_.Value} })
        
        $global:ServicePids += $job.Id
        Start-Sleep 2
    }

    Start-Sleep 5
}

# ========== RESUMEN ==========
Write-Color "╔══════════════════════════════════════════════════════════════╗" $Green
Write-Color "║                    🎉 TODOS LOS SERVICIOS INICIADOS          ║" $Green
Write-Color "╚══════════════════════════════════════════════════════════════╝" $Green
Write-Host ""

Write-Color "📍 Endpoints disponibles:" $Blue
Write-Host "  Gateway (API Unificada):  http://localhost:5000"
Write-Host "  Identity Service:         http://localhost:5001"
Write-Host "  Orders Service:           http://localhost:5002"
Write-Host "  Delivery Service:         http://localhost:5003"
Write-Host "  Feedback Service:         http://localhost:5004"
Write-Host ""

Write-Color "📚 Swagger/OpenAPI:" $Blue
Write-Host "  Gateway:  http://localhost:5000/swagger"
Write-Host "  Identity: http://localhost:5001/swagger"
Write-Host "  Orders:   http://localhost:5002/swagger"
Write-Host "  Delivery: http://localhost:5003/swagger"
Write-Host "  Feedback: http://localhost:5004/swagger"
Write-Host ""

Write-Color "🔧 Infraestructura:" $Blue
Write-Host "  PostgreSQL Identity: localhost:5432 (deuna_identity)"
Write-Host "  PostgreSQL Orders:   localhost:5433 (deuna_orders)"
Write-Host "  PostgreSQL Delivery: localhost:5434 (deuna_delivery)"
Write-Host "  PostgreSQL Feedback: localhost:5435 (deuna_feedback)"
Write-Host "  RabbitMQ Management: http://localhost:15672 (deuna/rabbitmq_dev_2026)"
Write-Host "  Redis:               localhost:6379"
Write-Host ""

Write-Color "📝 Logs en tiempo real:" $Blue
Write-Host "  Get-Content logs/gateway.log -Wait"
Write-Host "  Get-Content logs/identity.log -Wait"
Write-Host "  Get-Content logs/orders.log -Wait"
Write-Host ""

Write-Color "Presiona Ctrl+C para detener todos los servicios..." $Yellow

# Mostrar logs del gateway por defecto
$global:TailJob = Start-Job -ScriptBlock { Get-Content (Join-Path $using:LogsDir "gateway.log") -Wait }
Wait-Job $global:TailJob