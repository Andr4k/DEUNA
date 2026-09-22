# BACKEND-701 — Validación de release backend

Parte de `main`. Añade k6 parametrizable para health, auth y rutas disponibles, smoke tests de Compose/gateway, revisión Swagger/OpenAPI y runbook de variables, resultados y rollback. Los umbrales son escritura p95 <500 ms y lectura p95 <200 ms; fallar health/routing/umbral debe dar salida no cero. No despliegues producción ni crees recursos cloud. Puede usar mocks para servicios aún incompletos.
