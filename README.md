# DEUNA Domicilios - Sistema de Gestión de Pedidos

Sistema de microservicios para gestión de pedidos, delivery y feedback en tiempo real.

## 🏗️ Arquitectura

Este proyecto implementa una arquitectura de microservicios con:

- **4 Microservicios independientes** (.NET 10)
- **PostgreSQL** para persistencia (una BD por servicio)
- **Redis** para tracking GPS en tiempo real
- **RabbitMQ** para comunicación asíncrona entre servicios
- **Vertical Slice Architecture** para organización del código

## 📦 Microservicios

### 1. Deuna.Identity.Service
Gestión de autenticación y autorización (JWT) para restaurantes, repartidores y administradores.

### 2. Deuna.Orders.Service
Creación y gestión del ciclo de vida de pedidos con cálculo de distancias usando PostGIS.

### 3. Deuna.Delivery.Service
Asignación de repartidores, tracking GPS en vivo y gestión de entregas.

### 4. Deuna.Feedback.Service
Motor de feedback escalonado (3 niveles) con viralidad en WhatsApp.

## 🚀 Inicio Rápido

### Prerrequisitos

- .NET 10 SDK
- Docker y Docker Compose
- PostgreSQL 16
- Redis 7
- RabbitMQ 3.12

### Compilar la solución

```bash
dotnet build
```

### Ejecutar tests

```bash
dotnet test
```

## 📁 Estructura del Proyecto

```
app-pedidos/
├── Deuna.Identity.Service/
│   ├── Features/          # Casos de uso (Vertical Slice)
│   ├── Models/            # Entidades de dominio
│   ├── Consumers/         # Consumidores de eventos
│   ├── Publishers/        # Publicadores de eventos
│   └── Examples/          # Archivos .http para testing
├── Deuna.Orders.Service/
├── Deuna.Delivery.Service/
├── Deuna.Feedback.Service/
├── AGENTS.md              # Reglas de desarrollo
└── DOCUMENTACION_ARQUITECTURA.md
```

## 📚 Documentación

- **Arquitectura completa**: Ver `DOCUMENTACION_ARQUITECTURA.md`
- **Reglas de desarrollo**: Ver `AGENTS.md`
- **Vault Obsidian**: `/mnt/c/Users/Andres David/Documents/Proyects-Software/DEUNA Domicilios/`

## 🔄 Flujo de Trabajo

1. Crear rama: `git checkout -b feature/TASK-XXX-descripcion`
2. Implementar con TDD (Red-Green-Refactor)
3. Commits atómicos siguiendo Conventional Commits
4. Push y crear Merge Request

## 🧪 Stack Tecnológico

- **.NET 10** - Framework principal
- **Entity Framework Core 8** - ORM
- **MassTransit** - Mensajería asíncrona
- **FluentValidation** - Validaciones
- **Serilog** - Logging estructurado
- **BCrypt.Net** - Hashing de contraseñas
- **PostgreSQL + PostGIS** - Base de datos con extensiones geoespaciales
- **Redis** - Cache y tracking GPS
- **RabbitMQ** - Message broker

## 📝 Estado del Proyecto

**Sprint Actual**: Sprint 0 (Infraestructura)  
**Última Actualización**: 2026-09-18  
**Tareas Completadas**: TASK-001 ✅

## 📄 Licencia

Proyecto privado - DEUNA Domicilios © 2026