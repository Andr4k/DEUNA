# 🐳 Docker Compose - DEUNA Domicilios

Infraestructura de desarrollo local con PostgreSQL, PostGIS, Redis y RabbitMQ.

## 📦 Servicios Incluidos

| Servicio | Imagen | Puerto(s) | Descripción |
|:---------|:-------|:----------|:------------|
| **postgres-identity** | postgres:16-alpine | 5432 | Base de datos Identity Service |
| **postgres-orders** | postgis/postgis:16-3.4 | 5433 | Base de datos Orders Service + PostGIS |
| **postgres-delivery** | postgres:16-alpine | 5434 | Base de datos Delivery Service |
| **postgres-feedback** | postgres:16-alpine | 5435 | Base de datos Feedback Service |
| **redis** | redis:7-alpine | 6379 | Cache y tracking GPS |
| **rabbitmq** | rabbitmq:3.12-management | 5672, 15672 | Message broker + Management UI |

## 🚀 Inicio Rápido

### 1. Verificar que `.env` existe

El archivo `.env` ya está creado con valores de desarrollo. Si no existe:

```bash
cp .env.example .env
```

### 2. Levantar todos los servicios

```bash
docker-compose up -d
```

### 3. Verificar estado de los servicios

```bash
docker-compose ps
```

Todos los servicios deben mostrar estado `healthy` después de ~30 segundos.

### 4. Ver logs de un servicio específico

```bash
# Ver logs de RabbitMQ
docker-compose logs -f rabbitmq

# Ver logs de PostgreSQL Orders (con PostGIS)
docker-compose logs -f postgres-orders
```

## 🔍 Verificar Servicios

### PostgreSQL (Identity)
```bash
docker exec -it deuna-postgres-identity psql -U deuna_user -d deuna_identity -c "SELECT version();"
```

### PostgreSQL Orders con PostGIS
```bash
docker exec -it deuna-postgres-orders psql -U deuna_user -d deuna_orders -c "SELECT PostGIS_Version();"
```

### Redis
```bash
docker exec -it deuna-redis redis-cli -a redis_dev_2026 ping
# Respuesta esperada: PONG
```

### RabbitMQ Management UI
Abre en tu navegador: http://localhost:15672
- **Usuario:** `deuna`
- **Contraseña:** `rabbitmq_dev_2026` (ver `.env`)

## 🛠️ Comandos Útiles

### Detener todos los servicios
```bash
docker-compose stop
```

### Detener y eliminar contenedores (mantiene volúmenes)
```bash
docker-compose down
```

### Eliminar todo (contenedores + volúmenes)
```bash
docker-compose down -v
```

### Reiniciar un servicio específico
```bash
docker-compose restart postgres-orders
```

### Ver estadísticas de recursos
```bash
docker stats
```

## 📊 Healthchecks Configurados

Todos los servicios tienen healthchecks automáticos:

- **PostgreSQL**: `pg_isready` cada 10s
- **Redis**: `redis-cli ping` cada 10s
- **RabbitMQ**: `rabbitmq-diagnostics ping` cada 30s

Puedes verificar el estado con:
```bash
docker-compose ps
```

## 🗄️ Persistencia de Datos

Los datos se persisten en volúmenes Docker:

- `postgres-identity-data`
- `postgres-orders-data`
- `postgres-delivery-data`
- `postgres-feedback-data`
- `redis-data`
- `rabbitmq-data`
- `rabbitmq-log`

Para limpiar completamente y empezar de cero:
```bash
docker-compose down -v
docker-compose up -d
```

## 🔐 Seguridad

⚠️ **IMPORTANTE**: El archivo `.env` contiene credenciales y **NO debe committearse a Git**.

Ya está incluido en `.gitignore`. Verifica con:
```bash
git check-ignore .env
# Debe mostrar: .env
```

## 🌐 Red Docker

Todos los servicios están en la red `deuna-network` (bridge) para comunicación interna.

Los microservicios pueden conectarse usando:
- `postgres-identity:5432`
- `postgres-orders:5432`
- `postgres-delivery:5432`
- `postgres-feedback:5432`
- `redis:6379`
- `rabbitmq:5672`

## 📝 Conexión desde los Microservicios

### appsettings.Development.json (ejemplo para Identity Service)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=deuna_identity;Username=deuna_user;Password=deuna_dev_2026"
  },
  "Redis": {
    "Configuration": "localhost:6379,password=redis_dev_2026"
  },
  "RabbitMQ": {
    "Host": "localhost",
    "Port": 5672,
    "Username": "deuna",
    "Password": "rabbitmq_dev_2026",
    "VirtualHost": "deuna"
  }
}
```

## 🐛 Troubleshooting

### Error: "port is already allocated"
Verifica que los puertos no estén en uso:
```bash
# Linux/WSL
netstat -tulpn | grep -E '5432|5433|5434|5435|6379|5672|15672'

# Windows
netstat -ano | findstr "5432 5433 5434 5435 6379 5672 15672"
```

### Error: "unhealthy" en algún servicio
Ver logs específicos:
```bash
docker-compose logs <servicio>
```

### Redis rechaza conexiones
Verifica que la contraseña en `.env` coincida con la que usas al conectar.

## 📚 Referencias

- [PostgreSQL Docker Hub](https://hub.docker.com/_/postgres)
- [PostGIS Docker Hub](https://hub.docker.com/r/postgis/postgis)
- [Redis Docker Hub](https://hub.docker.com/_/redis)
- [RabbitMQ Docker Hub](https://hub.docker.com/_/rabbitmq)
- [Docker Compose Documentation](https://docs.docker.com/compose/)

---

**Última actualización:** 2026-09-18  
**Versión Docker Compose:** 3.9  
**TASK:** TASK-002
