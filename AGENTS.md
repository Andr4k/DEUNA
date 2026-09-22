# 🤖 AGENTS.md — Reglas de Desarrollo para Agentes de Código

> **Propósito:** Este documento define las reglas obligatorias de implementación, flujo de trabajo Git, estrategia de testing y sincronización con la documentación en Obsidian para todos los agentes (humanos o IA) que trabajen en el proyecto DEUNA Domicilios.

---

## 📋 Tabla de Contenidos

1. [Flujo de Trabajo Git](#flujo-de-trabajo-git)
2. [Estrategia de Commits Atómicos](#estrategia-de-commits-atómicos)
3. [Desarrollo Incremental (TDD)](#desarrollo-incremental-tdd)
4. [Testing Obligatorio](#testing-obligatorio)
5. [Ejemplos HTTP para API Testing](#ejemplos-http-para-api-testing)
6. [Sincronización con Obsidian](#sincronización-con-obsidian)
7. [Checklist Pre-Merge Request](#checklist-pre-merge-request)
8. [Contexto de Referencia Obligatorio](#contexto-de-referencia-obligatorio)

---

## 1. Flujo de Trabajo Git

### 1.1. Branching Strategy

**REGLA FUNDAMENTAL:** Cada nueva implementación DEBE crearse en una rama dedicada.

```bash
# Nomenclatura de ramas
feature/<task-id>-<descripcion-corta>
fix/<issue-id>-<descripcion-corta>
refactor/<descripcion-corta>

# Ejemplos válidos
feature/TASK-101-identity-models
feature/TASK-102-register-restaurant
fix/ISSUE-045-jwt-expiration
refactor/orders-validation
```

### 1.2. Ciclo de Vida de una Rama

```bash
# 1. Crear rama desde main (siempre actualizada)
git checkout main
git pull origin main
git checkout -b feature/TASK-102-register-restaurant

# 2. Implementar con commits atómicos (ver sección 2)
# ... desarrollo incremental ...

# 3. Push al finalizar CADA tarea completada
git push -u origin feature/TASK-102-register-restaurant

# 4. Crear Merge Request (MR) / Pull Request (PR)
# Título: "[TASK-102] Implement RegisterRestaurant endpoint"
# Descripción: Referenciar SPEC-001, criterios de éxito cumplidos, tests añadidos
```

### 1.3. Merge Request (MR) / Pull Request (PR)

**Plantilla Obligatoria:**

```markdown
## Tarea Relacionada
- **Task ID:** TASK-102
- **Spec:** SPEC-001 (Identity & Authentication)
- **Prioridad:** P1 (MVP)

## Cambios Implementados
- [ ] Endpoint POST `/api/v1/identity/register/restaurant` funcional
- [ ] Validación FluentValidation (email, contraseña, NIT único)
- [ ] Hashing de contraseña con BCrypt (cost 12)
- [ ] Generación de JWT con claims correctos

## Tests Añadidos
- [x] `RegisterRestaurant_WithValidData_ReturnsJwtToken` (Unit)
- [x] `RegisterRestaurant_WithDuplicateEmail_Returns409Conflict` (Integration)
- [x] `RegisterRestaurant_WithInvalidNIT_Returns400BadRequest` (Unit)

## Ejemplos HTTP
Archivo: `Deuna.Identity.Service/Examples/register-restaurant.http`

## Documentación Actualizada
- [x] Bitácora de Obsidian actualizada con progreso
- [x] Diagrama de secuencia actualizado (si aplica)

## Criterios de Éxito Cumplidos
- [x] SC-001.1: Registro completa en < 2s (p95) ✅ Validado con k6
- [x] SC-001.4: Email duplicado retorna HTTP 409 ✅ Test pasa

## Checklist Pre-Merge
- [x] Todos los tests pasan localmente (`dotnet test`)
- [x] Build exitoso sin warnings (`dotnet build`)
- [x] Code coverage ≥ 80% en clases nuevas
- [x] Documentación en Obsidian actualizada
- [x] Archivo `.http` con ejemplos añadido
```

---

## 2. Estrategia de Commits Atómicos

### 2.1. Principio de Atomicidad

**REGLA:** Un commit debe representar **una unidad lógica de cambio completa y funcional**.

❌ **MAL (Commit demasiado grande):**
```bash
git commit -m "feat: implement identity service"
# 50 archivos modificados, 3000 líneas agregadas
```

✅ **BIEN (Commits atómicos):**
```bash
# Commit 1: Modelos
git add Deuna.Identity.Service/Models/Usuario.cs
git add Deuna.Identity.Service/Models/PerfilRestaurante.cs
git commit -m "feat(identity): add Usuario and PerfilRestaurante models [TASK-101]"

# Commit 2: Migración
git add Deuna.Identity.Service/Migrations/20260918_InitialIdentitySchema.cs
git commit -m "feat(identity): add initial migration for identity schema [TASK-101]"

# Commit 3: Validador
git add Deuna.Identity.Service/Features/RegisterRestaurant/Validator.cs
git commit -m "feat(identity): add RegisterRestaurantValidator with FluentValidation [TASK-102]"

# Commit 4: Handler
git add Deuna.Identity.Service/Features/RegisterRestaurant/Handler.cs
git commit -m "feat(identity): implement RegisterRestaurantHandler with JWT generation [TASK-102]"

# Commit 5: Tests
git add Deuna.Identity.Service.Tests/Features/RegisterRestaurant/Tests.cs
git commit -m "test(identity): add unit tests for RegisterRestaurant [TASK-102]"

# Commit 6: Endpoint
git add Deuna.Identity.Service/Program.cs
git commit -m "feat(identity): register POST /api/v1/identity/register/restaurant endpoint [TASK-102]"

# Commit 7: Ejemplo HTTP
git add Deuna.Identity.Service/Examples/register-restaurant.http
git commit -m "docs(identity): add HTTP example for RegisterRestaurant [TASK-102]"
```

### 2.2. Convención de Mensajes de Commit (Conventional Commits)

```
<tipo>(<scope>): <descripción corta> [TASK-XXX]

<cuerpo opcional: explicación detallada del cambio>

<footer opcional: breaking changes, issues cerrados>
```

**Tipos válidos:**
- `feat`: Nueva funcionalidad
- `fix`: Corrección de bug
- `refactor`: Refactorización sin cambio de comportamiento
- `test`: Añadir o modificar tests
- `docs`: Cambios solo en documentación
- `chore`: Tareas de mantenimiento (deps, config)
- `perf`: Mejoras de performance

**Scopes válidos:**
- `identity`, `orders`, `delivery`, `feedback`, `gateway`, `shared`

**Ejemplos:**
```bash
feat(orders): implement CreateOrder endpoint with PostGIS distance calculation [TASK-202]
fix(delivery): correct Redis GPS tracking key expiration [ISSUE-034]
test(feedback): add integration tests for SubmitBasicFeedback [TASK-401]
docs(obsidian): update bitacora with Sprint 1 progress
refactor(identity): extract JWT generation to shared service
```

---

## 3. Desarrollo Incremental (TDD)

### 3.1. Ciclo Red-Green-Refactor OBLIGATORIO

**REGLA:** Antes de escribir código de producción, escribir el test que falla.

```csharp
// 1. RED: Escribir test que falla
[Fact]
public async Task RegisterRestaurant_WithValidData_ReturnsJwtToken()
{
    // Arrange
    var request = new RegisterRestaurantRequest { /* ... */ };

    // Act
    var response = await _client.PostAsJsonAsync("/api/v1/identity/register/restaurant", request);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var result = await response.Content.ReadFromJsonAsync<RegisterResponse>();
    result.Should().NotBeNull();
    result!.Token.Should().NotBeNullOrEmpty();
}

// Ejecutar: dotnet test → FALLA (endpoint no existe)
// ✅ Commit: "test(identity): add failing test for RegisterRestaurant [TASK-102]"

// 2. GREEN: Implementar código mínimo para que pase
public class RegisterRestaurantHandler
{
    public async Task<Result<RegisterResponse>> Handle(RegisterRestaurantRequest request)
    {
        // Implementación mínima pero correcta
        var user = new Usuario { /* ... */ };
        await _db.SaveChangesAsync();
        var token = _jwtService.GenerateToken(user);
        return Result.Ok(new RegisterResponse { Token = token });
    }
}

// Ejecutar: dotnet test → PASA
// ✅ Commit: "feat(identity): implement RegisterRestaurantHandler [TASK-102]"

// 3. REFACTOR: Mejorar código manteniendo tests en verde
public class RegisterRestaurantHandler
{
    public async Task<Result<RegisterResponse>> Handle(RegisterRestaurantRequest request)
    {
        // Extraer lógica a métodos privados
        var validationResult = await ValidateUniqueEmail(request.Email);
        if (!validationResult.IsSuccess) return validationResult;

        var user = await CreateUserWithProfile(request);
        var token = GenerateJwtToken(user);
        
        return Result.Ok(new RegisterResponse { Token = token, UserId = user.Id });
    }
}

// Ejecutar: dotnet test → SIGUE PASANDO
// ✅ Commit: "refactor(identity): extract validation and token generation methods [TASK-102]"
```

### 3.2. Secuencia de Implementación por Tarea

Para cada `TASK-XXX`:

1. **Leer contexto:**
   - Spec correspondiente en Obsidian `07. Spec-Kit (SDD)/01-specifications.md`
   - Criterios de aceptación en `07. Spec-Kit (SDD)/03-tasks-graph.md`
   - ADRs relevantes en `06. Bitacora y Registro de Decisiones/`

2. **Crear rama:**
   ```bash
   git checkout -b feature/TASK-XXX-descripcion
   ```

3. **TDD Loop (múltiples commits):**
   ```
   RED → Commit test
   GREEN → Commit implementation
   REFACTOR → Commit improvement
   ```

4. **Añadir ejemplo HTTP:**
   ```bash
   git add Examples/xxx.http
   git commit -m "docs(xxx): add HTTP example [TASK-XXX]"
   ```

5. **Actualizar Obsidian:**
   ```bash
   # Editar bitácora en Obsidian
   git commit -m "docs(obsidian): update bitacora with TASK-XXX completion"
   ```

6. **Push y MR:**
   ```bash
   git push -u origin feature/TASK-XXX-descripcion
   # Crear MR con plantilla obligatoria
   ```

---

## 4. Testing Obligatorio

### 4.1. Niveles de Testing Requeridos

| Nivel | Herramienta | Cobertura Mínima | Cuándo Ejecutar |
|:---|:---|:---:|:---|
| **Unit Tests** | xUnit + FluentAssertions | ≥ 80% | Cada commit |
| **Integration Tests** | xUnit + TestContainers | 100% flujos críticos | Cada feature completa |
| **E2E Tests** | Playwright / REST Assured | 100% user scenarios P1 | Antes de MR |

### 4.2. Estructura de Tests

```
Deuna.Identity.Service.Tests/
├── Unit/
│   ├── Validators/
│   │   └── RegisterRestaurantValidatorTests.cs
│   ├── Handlers/
│   │   └── RegisterRestaurantHandlerTests.cs
│   └── Services/
│       └── JwtServiceTests.cs
├── Integration/
│   ├── Endpoints/
│   │   └── RegisterRestaurantEndpointTests.cs
│   └── Database/
│       └── IdentityDbContextTests.cs
└── E2E/
    └── Scenarios/
        └── UserRegistrationFlowTests.cs
```

### 4.3. Test Naming Convention

```csharp
[Fact]
public async Task MethodName_StateUnderTest_ExpectedBehavior()
{
    // Ejemplo:
    // RegisterRestaurant_WithDuplicateEmail_Returns409Conflict
    // ValidateToken_WithExpiredJwt_Returns401Unauthorized
    // CalculateDistance_BetweenTwoPoints_ReturnsCorrectKilometers
}
```

### 4.4. Comandos de Ejecución

```bash
# Unit tests (rápido, sin infraestructura)
dotnet test --filter "Category=Unit"

# Integration tests (requiere Docker)
docker-compose up -d postgres-identity redis rabbitmq
dotnet test --filter "Category=Integration"

# Todos los tests
dotnet test

# Con reporte de cobertura
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
```

---

## 5. Ejemplos HTTP para API Testing

### 5.1. Estructura de Archivos `.http`

**REGLA:** Cada endpoint implementado DEBE tener un ejemplo `.http` funcional.

```
Deuna.Identity.Service/Examples/
├── register-restaurant.http
├── register-rider.http
├── login.http
└── refresh-token.http
```

### 5.2. Formato de Ejemplo HTTP

```http
### 1. Register Restaurant (Success Case)
POST {{baseUrl}}/api/v1/identity/register/restaurant
Content-Type: application/json

{
  "email": "nuevo@restaurante.com",
  "telefono": "+573001234567",
  "password": "SecurePass123!",
  "razonSocial": "Restaurante Demo S.A.S.",
  "nombreComercial": "Demo Restaurant",
  "nit": "900123456-7",
  "direccionSede": "Calle 45 #23-10",
  "ciudad": "Bogotá",
  "latitud": 4.6097100,
  "longitud": -74.0817500
}

### Expected Response (201 Created)
# {
#   "userId": "a1b2c3d4-5e6f-7g8h-9i0j-k1l2m3n4o5p6",
#   "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
#   "expiresIn": 28800
# }

###

### 2. Register Restaurant (Duplicate Email - Error Case)
POST {{baseUrl}}/api/v1/identity/register/restaurant
Content-Type: application/json

{
  "email": "nuevo@restaurante.com",
  "telefono": "+573009876543",
  "password": "AnotherPass456!",
  "razonSocial": "Otro Restaurante",
  "nombreComercial": "Otro",
  "nit": "900999888-1",
  "direccionSede": "Carrera 10 #20-30",
  "ciudad": "Medellín",
  "latitud": 6.2442,
  "longitud": -75.5812
}

### Expected Response (409 Conflict)
# {
#   "error": "EmailAlreadyRegistered",
#   "message": "El email 'nuevo@restaurante.com' ya está registrado"
# }

###

### 3. Login with Registered Restaurant
POST {{baseUrl}}/api/v1/identity/login
Content-Type: application/json

{
  "email": "nuevo@restaurante.com",
  "password": "SecurePass123!"
}

### Expected Response (200 OK)
# {
#   "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
#   "expiresIn": 28800,
#   "userId": "a1b2c3d4-5e6f-7g8h-9i0j-k1l2m3n4o5p6",
#   "role": "RESTAURANT"
# }
```

### 5.3. Variables de Entorno

```bash
# Crear archivo http-client.env.json (gitignored)
{
  "dev": {
    "baseUrl": "http://localhost:5001",
    "testEmail": "test@restaurant.com",
    "testPassword": "TestPass123!"
  },
  "staging": {
    "baseUrl": "https://api-staging.deuna.com",
    "testEmail": "staging@test.com",
    "testPassword": "StagingPass123!"
  }
}
```

---

## 6. Sincronización con Obsidian

### 6.1. REGLA FUNDAMENTAL: Reflejar Progreso Real

**OBLIGATORIO:** Al finalizar cada tarea, actualizar la bitácora en Obsidian.

**Ruta:** `/mnt/c/Users/Andres David/Documents/Proyects-Software/DEUNA Domicilios/`

### 6.2. Qué Actualizar

#### A. Bitácora de Tareas

**Archivo:** `06. Bitacora y Registro de Decisiones/Bitacora de Tareas y Evolucion.md`

```markdown
| **2026-09-18** | **TASK-102: Endpoint RegisterRestaurant** | ✅ Completado | MR #12, 7 commits, 85% coverage |
```

#### B. Spec-Kit: Marcar Tareas Completadas

**Archivo:** `07. Spec-Kit (SDD)/03-tasks-graph.md`

```markdown
### TASK-102: Implementar Feature `RegisterRestaurant`
**Estado:** ✅ COMPLETADO (2026-09-18)  
**MR:** #12  
**Commits:** 7

**Criterios de Aceptación:**
- [x] Endpoint implementado con Minimal API
- [x] Validación FluentValidation completa
- [x] Tests unitarios + integración (8 tests, 85% coverage)
- [x] Ejemplo HTTP funcional
```

#### C. Estado de Implementación

**Archivo:** `02. Arquitectura de Sistema/Arquitectura General de Microservicios.md`

```markdown
## Estado de Implementación

| Microservicio | Progreso | Endpoints | Última Actualización |
|:---|:---:|:---|:---|
| **Identity Service** | 40% | `/register/restaurant`, `/register/rider` | 2026-09-18 |
| **Orders Service** | 0% | - | - |
```

---

## 7. Checklist Pre-Merge Request

Antes de crear el MR, verificar:

### 7.1. Código
- [ ] Todos los tests pasan (`dotnet test`)
- [ ] Build sin warnings (`dotnet build -warnaserror`)
- [ ] Coverage ≥ 80% en código nuevo
- [ ] Commits atómicos (< 200 líneas por commit)
- [ ] Sin código comentado o TODOs sin issue

### 7.2. Testing
- [ ] Tests unitarios para lógica de negocio
- [ ] Tests de integración para endpoints
- [ ] Tests cubren casos de error (400, 401, 409, etc.)
- [ ] Nombres descriptivos (`Method_State_Behavior`)

### 7.3. Documentación
- [ ] Archivo `.http` con ejemplos
- [ ] Bitácora de Obsidian actualizada
- [ ] Spec-Kit actualizado con criterios cumplidos

### 7.4. Git
- [ ] Rama actualizada con `main`
- [ ] Sin conflictos de merge
- [ ] Commits siguen Conventional Commits

---

## 8. Contexto de Referencia Obligatorio

### 8.1. Antes de Implementar

**LEER en este orden:**

1. **Spec en Obsidian:**
   ```
   /mnt/c/Users/Andres David/Documents/Proyects-Software/DEUNA Domicilios/
   07. Spec-Kit (SDD)/01-specifications.md → SPEC-00X
   ```

2. **Tarea específica:**
   ```
   07. Spec-Kit (SDD)/03-tasks-graph.md → TASK-XXX
   ```

3. **ADRs relacionados:**
   ```
   06. Bitacora y Registro de Decisiones/ADR-00X
   ```

4. **Constitución:**
   ```
   07. Spec-Kit (SDD)/00-constitution.md
   ```

### 8.2. Durante Implementación

- **Modelo de datos:** `04. Dominio y Modelos de Datos/`
- **Contratos API:** `07. Spec-Kit (SDD)/02-technical-plan.md` → Sección 3
- **Patrones:** `02. Arquitectura de Sistema/Patrones y Directrices de Diseno C#.md`

---

## 9. Ejemplo de Flujo Completo

```bash
# 1. Leer contexto en Obsidian
# - SPEC-001 → US-001.1
# - TASK-102 → Criterios de aceptación
# - ADR-001 → Modelo de identidad

# 2. Crear rama
git checkout main && git pull
git checkout -b feature/TASK-102-register-restaurant

# 3. TDD: RED
# Escribir test que falla
git add Tests/
git commit -m "test(identity): add failing validator tests [TASK-102]"

# 4. TDD: GREEN
# Implementar código
git add Features/RegisterRestaurant/Validator.cs
git commit -m "feat(identity): implement RegisterRestaurantValidator [TASK-102]"

# 5. TDD: Handler
git add Features/RegisterRestaurant/Handler.cs
git commit -m "feat(identity): implement RegisterRestaurantHandler [TASK-102]"

# 6. Integration Test
git add Tests/Integration/
git commit -m "test(identity): add integration tests [TASK-102]"

# 7. Endpoint
git add Program.cs
git commit -m "feat(identity): register endpoint [TASK-102]"

# 8. Ejemplo HTTP
git add Examples/register-restaurant.http
git commit -m "docs(identity): add HTTP example [TASK-102]"

# 9. Actualizar Obsidian
# Editar bitácora manualmente en Obsidian
git add "/mnt/c/Users/Andres David/Documents/Proyects-Software/DEUNA Domicilios/"
git commit -m "docs(obsidian): update progress for TASK-102"

# 10. Push y MR
git push -u origin feature/TASK-102-register-restaurant
# Crear MR con plantilla
```

---

## 10. Comandos de Referencia Rápida

```bash
# Crear rama
git checkout -b feature/TASK-XXX-descripcion

# Tests antes de commit
dotnet test

# Commit atómico
git add <archivos>
git commit -m "tipo(scope): descripción [TASK-XXX]"

# Push
git push -u origin feature/TASK-XXX-descripcion

# Actualizar con main
git rebase main

# Ver cobertura
dotnet test /p:CollectCoverage=true

# Limpiar builds
dotnet clean && rm -rf */bin */obj
```

---

## 11. Violaciones Comunes

| ❌ Violación | ✅ Solución |
|:---|:---|
| Commit gigante | Commits atómicos por archivo/funcionalidad |
| Push sin tests | `dotnet test` antes de push |
| MR sin ejemplos HTTP | Añadir `.http` en `Examples/` |
| No leer Spec | Leer SPEC-00X completo antes de empezar |
| Obsidian desactualizado | Actualizar bitácora ANTES del push final |
| Tests sin nombres claros | Usar `Method_State_Behavior` |
| Branch a `main` directo | SIEMPRE feature branch |
| Merge sin review | Esperar aprobación del MR |

---

**Última Actualización:** 2026-09-21  
**Versión:** 1.1.0  
**Ruta Obsidian:** `/mnt/c/Users/Andres David/Documents/Proyects-Software/DEUNA Domicilios/`  
**Proyecto:** DEUNA Domicilios & Feedback Engine

---

## 12. Estado Actual del Proyecto (2026-09-21)

### ✅ Sprint 0 - Infraestructura (COMPLETADO)

| Tarea | Estado | Branch | MR | Commits | Tiempo Real |
|-------|--------|--------|-----|---------|-------------|
| TASK-001: Solución .NET 9 + 6 microservicios | ✅ COMPLETADO | `feature/TASK-001` | #1 | 6 | 2.5h |
| TASK-002: Docker Compose (PostgreSQL x4, Redis, RabbitMQ) | ✅ COMPLETADO | `feature/TASK-002` | #2 | 4 | 1.5h |
| TASK-003: API Gateway YARP + JWT + Rate Limit | ✅ COMPLETADO | `feature/TASK-003` | #5 | 6 | 3.5h |
| TASK-004: Middleware JWT Compartido (Deuna.Shared) | ✅ COMPLETADO | (en TASK-003) | #5 | 3 | 2h |

**Infraestructura Docker Compose:**
- 6 microservicios: Identity, Gateway, Orders, Delivery, Feedback
- 4 PostgreSQL (Identity, Orders/PostGIS, Delivery, Feedback)
- Redis 7 (puerto 6379)
- RabbitMQ 3.13 Management UI (5672/15672)

**Dockerfiles Multi-stage (.NET 9 Alpine):**
- Build context raíz con `.dockerignore` global
- Cada servicio copia solo sus fuentes necesarias
- Usuario no-root (appuser:1001)
- Health checks RabbitMQ v9 con factory async

### ✅ Sprint 1 - Identity Service (COMPLETADO)

| Tarea | Estado | Branch | MR | Commits | Tiempo Real |
|-------|--------|--------|-----|---------|-------------|
| TASK-101: Modelos & Migraciones Identity DB | ✅ COMPLETADO | `feature/TASK-101` | #3 | 4 | 3h |
| TASK-102: RegisterRestaurant + RegisterRider | ✅ COMPLETADO | `feature/TASK-102` | #3 | 7 | 5h |
| TASK-103: Login & JWT (TDD) | ✅ COMPLETADO | `feature/TASK-103` | #4 | 5 | 3h |

**Endpoints Identity:**
- `POST /api/v1/identity/register/restaurant` ✅
- `POST /api/v1/identity/register/rider` ✅
- `POST /api/v1/identity/login` (TDD 6 tests, 100% coverage) ✅

**Especs Login cumplidas:**
- Rate limiting: 5 req/min por IP
- JWT 8h expiración fija
- HTTP 401 genérico (no enumera emails)
- HTTP 403 cuenta inactiva
- BCrypt cost 12
- Actualiza `ultimo_login`

### 📋 Próximas Tareas (Sprint 2-3)

| Tarea | Prioridad | Estimación | Dependencias |
|-------|-----------|------------|--------------|
| TASK-201: Modelos & Migraciones Orders DB (PostGIS) | P1 | 5h | TASK-101 |
| TASK-202: CreateOrder (4 pasos, QR, PostGIS distancias) | P1 | 8h | TASK-201 |
| TASK-301: Consumer PedidoCreado (Delivery) | P1 | 4h | TASK-202 |
| TASK-302: Tracking GPS Redis | P1 | 6h | TASK-301 |

### Comandos de Validación Actual

```bash
# Levantar stack completo
cd /mnt/c/Users/Andres\ David/Documents/Proyectos/app-pedidos
sudo docker-compose up -d

# Verificar servicios
sudo docker-compose ps
# Deben mostrar: 6 microservicios + 6 infra = 12 contenedores "healthy"

# Tests Identity
cd Deuna.Identity.Service.Tests
dotnet test --filter "FullyQualifiedName~LoginTests"

# Build completo
sudo docker-compose build --no-cache
```

### Endpoints Activos (via Gateway puerto 5000)

| Servicio | Endpoints | Puerto Directo |
|----------|-----------|----------------|
| Identity | `/api/v1/identity/register/restaurant`, `/register/rider`, `/login` | 5001 |
| Gateway  | Enruta todo `/api/v1/*` | 5000 |
| Orders   | `/api/v1/orders` (pendiente implementar) | 5002 |
| Delivery | `/api/v1/delivery` (pendiente) | 5003 |
| Feedback | `/api/v1/feedback` (pendiente) | 5004 |
