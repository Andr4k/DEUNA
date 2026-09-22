# BACKEND-102 — Identity: eventos v1

Parte de `main`; no necesita consumidores. Publica por MassTransit `UsuarioRegistrado` y `UsuarioActualizado` tras persistencia exitosa. Contrato: `usuarioId`, `rol`, `email`, `telefono`, `activo`, `occurredAt`; prohibidos hashes, JWT y refresh tokens. Añade publisher, integración en registro/actualización pertinente y pruebas InMemoryTestHarness: publicación única, contenido correcto y cero evento ante fallo. No cambies JWT ni implementes consumidores.
