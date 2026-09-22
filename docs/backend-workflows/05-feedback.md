# BACKEND-401 — Feedback, fondo y WhatsApp

Parte de `main`. Construye modelos/migraciones de encuestas, criterios, viralidad, fondo y transacciones; consumer idempotente de `PedidoEntregado` v1 y endpoints basic/detailed/share/resúmenes especificados. Mantén pedidos habilitados localmente, sin llamadas a Orders/Delivery. Nivel 2 sólo si comida ≥4; una encuesta básica por pedido; aporte de fondo idempotente. WhatsApp se implementa por interfaz con fake, sin cuenta externa. Pruebas TestHarness/persistencia prueban duplicados y secretos ausentes.
