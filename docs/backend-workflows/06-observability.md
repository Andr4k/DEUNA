# BACKEND-501 — Observabilidad backend

Parte de `main`, sin requerir flujos de negocio terminados. Instrumenta Gateway y servicios con Serilog JSON (`CorrelationId`, `ServiceName`), OpenTelemetry HTTP/HttpClient/MassTransit y métricas Prometheus. Configura Jaeger/Zipkin y Prometheus local sólo si preserva el stack. Contraseñas, hashes, JWT y QR no pueden aparecer en logs. Entrega pruebas de enriquecimiento/sanitización, documentación y evidencia de health/métricas.
