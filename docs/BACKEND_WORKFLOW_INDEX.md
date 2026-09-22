# Tareas backend independientes

Cada brief es autocontenido: parte de `main`, usa dobles de prueba para servicios aún inexistentes y no exige que otro agente termine. Todos requieren rama propia, TDD, `.http` cuando expongan endpoints, build/tests verdes y ≥80% de cobertura de lógica nueva. Se respetan VSA, Minimal APIs, database-per-service y no se publican secretos.

| ID | Archivo | Entregable |
|---|---|---|
| BACKEND-101 | [01-platform-ci.md](backend-workflows/01-platform-ci.md) | CI y runtime .NET 8 |
| BACKEND-102 | [02-identity-events.md](backend-workflows/02-identity-events.md) | Eventos Identity v1 |
| BACKEND-201 | [03-orders.md](backend-workflows/03-orders.md) | Orders P1 completo |
| BACKEND-301 | [04-delivery.md](backend-workflows/04-delivery.md) | Delivery P1 completo |
| BACKEND-401 | [05-feedback.md](backend-workflows/05-feedback.md) | Feedback/Fondo/WhatsApp |
| BACKEND-501 | [06-observability.md](backend-workflows/06-observability.md) | Logs, traces y métricas |
| BACKEND-601 | [07-security-contracts.md](backend-workflows/07-security-contracts.md) | Seguridad y contratos |
| BACKEND-701 | [08-release-validation.md](backend-workflows/08-release-validation.md) | Smoke, carga y runbook |

## Contratos v1

Los eventos son JSON camelCase. `PedidoCreado`: `pedidoId`, `restauranteId`, `codigoVisible`, `direccionEntrega`, `ciudad`, `latitudDestino`, `longitudDestino`, `tokenQrLocal`, `tokenQrEntrega`, `createdAt`. `PedidoAsignado`: `pedidoId`, `repartidorId`, `asignacionId`, `assignedAt`. `PedidoEnRuta`: `pedidoId`, `repartidorId`, `startedAt`. `PedidoEntregado`: `pedidoId`, `restauranteId`, `repartidorId`, `deliveredAt`. `UsuarioRegistrado` y `UsuarioActualizado`: `usuarioId`, `rol`, `email`, `telefono`, `activo`, `occurredAt`. Nunca incluir password, hash, JWT o refresh token.
