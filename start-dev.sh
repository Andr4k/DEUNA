#!/bin/bash
# start-dev.sh - Inicia todos los microservicios DEUNA en modo desarrollo
# Uso: ./start-dev.sh
# Requiere: Docker Desktop/Engine, .NET 10 SDK, WSL2 (en Windows)

set -e

# Colores para output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$ROOT_DIR/docker-compose.yml"
ENV_FILE="$ROOT_DIR/.env"

echo -e "${BLUE}╔══════════════════════════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║           DEUNA - Microservicios Development Stack           ║${NC}"
echo -e "${BLUE}╚══════════════════════════════════════════════════════════════╝${NC}"
echo ""

# Verificar .env
if [ ! -f "$ENV_FILE" ]; then
    echo -e "${YELLOW}⚠️  No existe .env, creando uno por defecto...${NC}"
    cat > "$ENV_FILE" << 'EOF'
# Database
POSTGRES_PASSWORD=deuna_dev_2026

# Redis
REDIS_PASSWORD=redis_dev_2026

# RabbitMQ
RABBITMQ_USER=deuna
RABBITMQ_PASSWORD=rabbitmq_dev_2026
EOF
    echo -e "${GREEN}✅ .env creado con valores por defecto${NC}"
fi

# Cargar variables de entorno
set -a
source "$ENV_FILE"
set +a

# Función para limpiar procesos al salir
cleanup() {
    echo -e "\n${YELLOW}🛑 Deteniendo servicios...${NC}"
    if [ ! -z "$GATEWAY_PID" ]; then kill $GATEWAY_PID 2>/dev/null; fi
    if [ ! -z "$IDENTITY_PID" ]; then kill $IDENTITY_PID 2>/dev/null; fi
    if [ ! -z "$ORDERS_PID" ]; then kill $ORDERS_PID 2>/dev/null; fi
    if [ ! -z "$DELIVERY_PID" ]; then kill $DELIVERY_PID 2>/dev/null; fi
    if [ ! -z "$FEEDBACK_PID" ]; then kill $FEEDBACK_PID 2>/dev/null; fi
    
    # Opcional: detener docker-compose también
    # docker-compose -f "$COMPOSE_FILE" down
    
    echo -e "${GREEN}✅ Servicios detenidos${NC}"
    exit 0
}

trap cleanup INT TERM

# ========== PASO 1: Infraestructura ==========
echo -e "${BLUE}📦 [1/4] Iniciando infraestructura (PostgreSQL, RabbitMQ, Redis)...${NC}"
docker-compose -f "$COMPOSE_FILE" up -d postgres-identity postgres-orders postgres-delivery postgres-feedback rabbitmq redis

echo -e "${YELLOW}⏳ Esperando healthchecks de infraestructura...${NC}"
# Esperar a que postgres-identity esté healthy
for i in {1..30}; do
    if docker-compose -f "$COMPOSE_FILE" ps postgres-identity | grep -q "healthy"; then
        echo -e "${GREEN}✅ PostgreSQL Identity listo${NC}"
        break
    fi
    sleep 2
    if [ $i -eq 30 ]; then
        echo -e "${RED}❌ Timeout esperando PostgreSQL Identity${NC}"
        exit 1
    fi
done

# Esperar RabbitMQ
for i in {1..30}; do
    if docker-compose -f "$COMPOSE_FILE" ps rabbitmq | grep -q "healthy"; then
        echo -e "${GREEN}✅ RabbitMQ listo${NC}"
        break
    fi
    sleep 2
    if [ $i -eq 30 ]; then
        echo -e "${RED}❌ Timeout esperando RabbitMQ${NC}"
        exit 1
    fi
done

echo -e "${GREEN}✅ Infraestructura lista${NC}"
echo ""

# ========== PASO 2: Build ==========
echo -e "${BLUE}🔨 [2/4] Compilando solución completa...${NC}"
cd "$ROOT_DIR"
if dotnet build Deuna.sln --no-incremental --verbosity quiet; then
    echo -e "${GREEN}✅ Build exitoso (0 errores)${NC}"
else
    echo -e "${RED}❌ Error en build${NC}"
    exit 1
fi
echo ""

# ========== PASO 3: Migraciones ==========
echo -e "${BLUE}🗄️  [3/4] Aplicando migraciones de base de datos...${NC}"
# Identity
cd "$ROOT_DIR/Deuna.Identity.Service"
if dotnet ef database update --no-build 2>/dev/null; then
    echo -e "${GREEN}✅ Migraciones Identity aplicadas${NC}"
else
    echo -e "${YELLOW}⚠️  Migraciones Identity: ya aplicadas o error (verificar)${NC}"
fi

# Orders, Delivery, Feedback - cuando tengan migraciones
# for svc in Orders Delivery Feedback; do
#     cd "$ROOT_DIR/Deuna.$svc.Service"
#     dotnet ef database update --no-build 2>/dev/null && echo "✅ Migraciones $svc aplicadas" || echo "⚠️  $svc: sin migraciones o error"
# done
echo ""

# ========== PASO 4: Microservicios ==========
echo -e "${BLUE}🚀 [4/4] Iniciando microservicios...${NC}"

# Gateway (puerto 5000) - Puerto expuesto para frontend
echo -e "  ${YELLOW}▶${NC} Gateway (puerto 5000)..."
cd "$ROOT_DIR/Deuna.Gateway"
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5000" \
    dotnet run --no-build --urls "http://localhost:5000" > "$ROOT_DIR/logs/gateway.log" 2>&1 &
GATEWAY_PID=$!

# Identity (puerto 5001)
echo -e "  ${YELLOW}▶${NC} Identity (puerto 5001)..."
cd "$ROOT_DIR/Deuna.Identity.Service"
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5001" \
    dotnet run --no-build --urls "http://localhost:5001" > "$ROOT_DIR/logs/identity.log" 2>&1 &
IDENTITY_PID=$!

# Orders (puerto 5002)
echo -e "  ${YELLOW}▶${NC} Orders (puerto 5002)..."
cd "$ROOT_DIR/Deuna.Orders.Service"
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5002" \
    dotnet run --no-build --urls "http://localhost:5002" > "$ROOT_DIR/logs/orders.log" 2>&1 &
ORDERS_PID=$!

# Delivery (puerto 5003)
echo -e "  ${YELLOW}▶${NC} Delivery (puerto 5003)..."
cd "$ROOT_DIR/Deuna.Delivery.Service"
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5003" \
    dotnet run --no-build --urls "http://localhost:5003" > "$ROOT_DIR/logs/delivery.log" 2>&1 &
DELIVERY_PID=$!

# Feedback (puerto 5004)
echo -e "  ${YELLOW}▶${NC} Feedback (puerto 5004)..."
cd "$ROOT_DIR/Deuna.Feedback.Service"
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5004" \
    dotnet run --no-build --urls "http://localhost:5004" > "$ROOT_DIR/logs/feedback.log" 2>&1 &
FEEDBACK_PID=$!

# Crear directorio de logs si no existe
mkdir -p "$ROOT_DIR/logs"

# Esperar un momento para que inicien
sleep 5

echo ""
echo -e "${GREEN}╔══════════════════════════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║                    🎉 TODOS LOS SERVICIOS INICIADOS          ║${NC}"
echo -e "${GREEN}╚══════════════════════════════════════════════════════════════╝${NC}"
echo ""
echo -e "${BLUE}📍 Endpoints disponibles:${NC}"
echo -e "  ${GREEN}Gateway (API Unificada):${NC}  http://localhost:5000"
echo -e "  ${GREEN}Identity Service:${NC}         http://localhost:5001"
echo -e "  ${GREEN}Orders Service:${NC}           http://localhost:5002"
echo -e "  ${GREEN}Delivery Service:${NC}         http://localhost:5003"
echo -e "  ${GREEN}Feedback Service:${NC}         http://localhost:5004"
echo ""
echo -e "${BLUE}📚 Documentación (Swagger/OpenAPI):${NC}"
echo -e "  ${GREEN}Gateway:${NC}  http://localhost:5000/swagger"
echo -e "  ${GREEN}Identity:${NC} http://localhost:5001/swagger"
echo -e "  ${GREEN}Orders:${NC}   http://localhost:5002/swagger"
echo -e "  ${GREEN}Delivery:${NC} http://localhost:5003/swagger"
echo -e "  ${GREEN}Feedback:${NC} http://localhost:5004/swagger"
echo ""
echo -e "${BLUE}🔧 Infraestructura:${NC}"
echo -e "  ${GREEN}PostgreSQL Identity:${NC} localhost:5432 (deuna_identity)"
echo -e "  ${GREEN}PostgreSQL Orders:${NC}   localhost:5433 (deuna_orders)"
echo -e "  ${GREEN}PostgreSQL Delivery:${NC} localhost:5434 (deuna_delivery)"
echo -e "  ${GREEN}PostgreSQL Feedback:${NC} localhost:5435 (deuna_feedback)"
echo -e "  ${GREEN}RabbitMQ Management:${NC} http://localhost:15672 (deuna/rabbitmq_dev_2026)"
echo -e "  ${GREEN}Redis:${NC}               localhost:6379"
echo ""
echo -e "${BLUE}📝 Logs en tiempo real:${NC}"
echo -e "  ${YELLOW}tail -f logs/gateway.log${NC}"
echo -e "  ${YELLOW}tail -f logs/identity.log${NC}"
echo -e "  ${YELLOW}tail -f logs/orders.log${NC}"
echo ""
echo -e "${YELLOW}Presiona Ctrl+C para detener todos los servicios...${NC}"
echo ""

# Mantener script vivo y mostrar logs del gateway por defecto
tail -f "$ROOT_DIR/logs/gateway.log" &
TAIL_PID=$!

wait $GATEWAY_PID $IDENTITY_PID $ORDERS_PID $DELIVERY_PID $FEEDBACK_PID
cleanup