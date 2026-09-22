# BACKEND-301 — Delivery P1 completo

Parte de `main`. Implementa modelo/migración de pedidos disponibles y asignaciones, consumer idempotente de `PedidoCreado` v1 y endpoints: disponibles, aceptar concurrentemente, QR local, iniciar ruta, GPS y QR de entrega. Usar DB local y Redis (`GEOADD`, consulta geoespacial, TTL 1h), sin consultar Orders. Publicar `PedidoAsignado`, `PedidoEnRuta`, `PedidoEntregado` v1. Usar TestHarness/Redis de prueba y JWT fake con `RIDER`. Aceptación: un pedido se acepta una vez, QR inválido no transiciona, GPS expira y cada transición publica un evento.
