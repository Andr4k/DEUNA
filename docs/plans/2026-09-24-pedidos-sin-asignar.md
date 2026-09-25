# Plan — Pantalla "Pedidos sin asignar"

**Objetivo:** que el administrador vea los pedidos que el sistema **no pudo asignar**
(y los que quedaron en manos de un repartidor que no responde) y pueda asignarlos a
mano, eligiendo entre los candidatos que el sistema ya calcula.

**Estado:** plan revisado contra el código (reconocimiento del servicio de Orders).
Las correcciones que salieron de ahí están marcadas con ⚠️.

---

## Dónde vive cada cosa (y por qué)

| Pieza | Servicio | Motivo |
| :--- | :--- | :--- |
| **Listar** los pedidos sin asignar | **Orders** | Orders es el dueño de los pedidos y ya tiene replicado todo lo que la lista necesita |
| **Asignar** a mano | **Delivery** ⚠️ | ⚠️ **No puede ir en Orders.** El evento `PedidoAsignado` lo publica Delivery, y Delivery es quien tiene el matching, la telemetría y `BuscarCandidatosAsync`. Orders solo publica `PedidoCreado`: ponerlo ahí obligaría a inventar un evento nuevo y dejaría el contrato partido en dos servicios |
| Ver los **candidatos** de un pedido | **Delivery** | Los candidatos salen de la telemetría en Redis, que es de Delivery |

Metrics queda afuera: es de solo lectura y no puede emitir la asignación.

**Orden de las rebanadas:** backend primero. La pantalla se conecta cuando el backend
responde con datos reales; así no mostramos una pantalla que parece real y no lo es.

---

## Rebanadas (un MR cada una)

| # | Rebanada | Servicio | Qué entra |
| :--- | :--- | :--- | :--- |
| **B1** | Listado de pedidos sin asignar | Orders | `GET /api/v1/admin/orders/sin-asignar` con filtros, paginación, prioridad por rango de precio y orden por valor |
| **B2** | Asignación manual | **Delivery** ⚠️ | `POST /api/v1/admin/delivery/asignar/{pedidoId}` con rol ADMIN, validando contra los candidatos reales |
| **B3** | Candidatos para un pedido | Delivery | `GET /api/v1/admin/delivery/candidatos/{pedidoId}` — los del radio, ordenados por distancia |
| **F1** | La pantalla | Portal | Tabla, filtros, paginación y los KPIs del encabezado |
| **F2** | El menú de domiciliarios | Portal | El panel de la segunda captura, con el botón Asignar |

```
B1 (Orders) ──> F1 (Portal, la tabla)
B2 (Delivery) ──> F2 (Portal, el menú)
B3 (Delivery) ──> F2
```

Cada PR declara su borde: qué empieza, qué termina, de qué depende y qué queda fuera.
Ninguna rebanada debería pasar las 400 líneas cambiadas; si alguna lo hace, la parto.

---

## B1 — El listado (lo que pediste)

```
GET /api/v1/admin/orders/sin-asignar ⚠️
    ?zona=&prioridad=&esperaMin=&incluirSinRespuesta=&pagina=&tamano=
→ 200 { items: [...], total, pagina, tamano }
```

⚠️ **Grupo nuevo.** El grupo `/api/v1/orders` existente exige
`.RequireAuthorization("restaurant")`, así que un administrador no entra. La política
`admin` existe en `Deuna.Shared` pero Orders no la usa en ninguna ruta todavía: la ruta
va en un grupo propio con política `admin`.

Por pedido: `codigo`, `restaurante`, `recogerEn`, `entregarEn`, `generadoEn`,
`minutosEsperando`, `prioridad`, `valorDomicilio`, `zona`.

**Reglas:**

1. **`sin-asignar` = estado `Buscando`.** Es el estado en el que el sistema deja un
   pedido cuando no encontró candidato dentro del radio. Existe por diseño: el propio
   `AsignacionBackgroundService` dice que *"si al crear el pedido no había nadie en el
   radio, el pedido no puede esperar indefinidamente a que alguien lo note"*.
2. **La prioridad sale del rango de precio del domicilio**, no del tiempo de espera.
   Los rangos van en **configuración**, no en el código: el radio de 5 km y el horario
   fijo del restaurante nacieron como constantes y hoy son un pendiente del proyecto.
   ⚠️ Orders **no tiene ninguna sección de configuración** todavía (`appsettings.json`
   solo trae `Logging` y `AllowedHosts`): hay que crear `OrdersOptions` y su sección.
3. **Orden por defecto:** valor descendente, después tiempo de espera descendente. Es
   lo que pediste: primero los de valor más alto.
4. **`incluirSinRespuesta`** suma los pedidos en `Asignado` cuya antigüedad de
   asignación supera un umbral **configurable**. El umbral queda **apagado y con valor
   pendiente**: el criterio se define cuando se construya la app móvil de
   domiciliarios. El filtro existe desde el principio para no tener que rehacer el
   endpoint después, pero no inventa el número.
5. **Rol ADMIN.** Ni RIDER ni RESTAURANT ven esta pantalla.
6. **Paginación con `total`** ⚠️: no existe **ningún** endpoint paginado en la solución
   (ni `Skip`, ni `Take`, ni `PageSize` en producción). El DTO `{ items, total, pagina,
   tamano }` se crea acá desde cero.

### De dónde sale cada dato (verificado en el código)

| Dato | Origen | Estado |
| :--- | :--- | :--- |
| Restaurante | `restaurantes_replicados.NombreComercial` | ✅ existe |
| Recoger en | `restaurantes_replicados.DireccionSede` + `Ciudad` | ✅ existe |
| Entregar en | `direcciones_entrega.Calle` + `Numero` + `Ciudad` | ✅ existe |
| Valor del domicilio | `tarifas_aplicadas.TotalCalculado` (y `pedidos.CostoEnvio`) | ✅ existe |
| Fecha de asignación | `pedidos_asignados_repartidor.FechaAsignacion` | ✅ existe |
| Minutos esperando | `UtcNow - pedidos.FechaCreacion` | calculado |
| Prioridad | ⚠️ **No existe como campo**: se deriva del valor por rango | a construir |

**Sin cruzar bases, y se puede:** las réplicas ya están en la base de Orders. El listado
se resuelve con un solo `DbContext`, sin hablar con otro servicio. Eso es lo que hace
viable esta pantalla en Orders.

**Un pendiente chico:** el **nombre del repartidor** no está en Orders (no hay réplica de
repartidores; viven en Delivery). En la lista de excepciones se muestra el pedido y su
antigüedad de asignación, que es lo que el operador necesita para decidir; el nombre se
resuelve cuando abra el desplegable (B3).

**TDD (obligatorio).** Primero los tests que fallan, después la implementación:

| Test | Qué prueba |
| :--- | :--- |
| `ListaSinAsignar_DevuelveSoloBuscando` | Un pedido `Asignado` no aparece en la lista |
| `ListaSinAsignar_OrdenaPorValorDescendente` | El de mayor valor va primero |
| `ListaSinAsignar_AplicaPrioridadPorRangoDePrecio` | Un domicilio caro sale "Alta" y uno barato "Baja", con los rangos de configuración |
| `ListaSinAsignar_FiltraPorZona` | El filtro por ciudad devuelve solo esa zona |
| `ListaSinAsignar_PaginaYDevuelveTotal` | `pagina=2&tamano=5` devuelve los correctos y el total completo |
| `ListaSinAsignar_SinUmbral_NoTraeSinRespuesta` | Con el umbral apagado no aparece ningún `Asignado` |
| `ListaSinAsignar_ConUmbral_TraeLosVencidos` | Con el umbral configurado aparece el que superó el tiempo |

Después de que pasen: **verificar que los tests muerdan** (mutación: romper el orden,
romper el filtro de estado, apagar el umbral a mano) — si no fallan, no prueban nada.

---

## B2 — La asignación manual ⚠️ (en Delivery, no en Orders)

```
POST /api/v1/admin/delivery/asignar/{pedidoId}   { repartidorId }
→ 200 | 400 (el pedido ya no está en Buscando) | 409 (el repartidor no es candidato)
```

⚠️ **Va en Delivery, aunque la pantalla sea de pedidos.** El evento `PedidoAsignado` lo
publica Delivery, y `IAsignacionService` —el que conoce el radio, la telemetría y los
candidatos— vive ahí. Orders solo publica `PedidoCreado`: emitir la asignación desde
Orders partiría el contrato en dos servicios y obligaría a inventar un evento que no
existe. Que el listado esté en Orders no significa que la asignación también.

⚠️ Hoy `IntentarAsignarAsync(pedidoId, excluirRepartidorId, ct)` **elige él** al candidato:
no acepta que le digan cuál. Hace falta una variante que reciba el `repartidorId` objetivo
y **valide que esté en la lista de candidatos**. Y no existe ningún endpoint ADMIN en
Delivery: los cinco actuales son de rol `rider` o anónimos.

**No salta ninguna regla.** El repartidor elegido tiene que estar **dentro del radio
del restaurante** y **libre** (sin otro pedido en `Asignado`, `ConfirmadoEnLocal` o
`EnRuta`). Esas son exactamente las reglas del matching automático; la asignación
manual es un **desempate humano sobre una lista que el sistema ya considera válida**,
no un bypass. Si el operador elige a alguien que no era candidato, el endpoint lo
rechaza y dice por qué.

Emite **el mismo `PedidoAsignado`** que la asignación automática. Orders lo consume
igual que siempre y no se entera de que hubo una mano humana.

**Cuidado con el hueco conocido:** hoy el reintento solo mira los pedidos en `Buscando`,
así que un pedido `Asignado` a alguien que no responde queda trabado. La asignación
manual no lo arregla; el arreglo es la cancelación del domiciliario, que se decidirá con
la app móvil.

---

## B3 — Los candidatos para un pedido

```
GET /api/v1/delivery/candidatos/{pedidoId}
→ 200 [ { repartidorId, nombre, distanciaKm, tiempoEstimadoMin, estado, zona, calificacion } ]
```

Reusa `BuscarCandidatosAsync`, que ya devuelve *"todos los repartidores dentro del
radio, ordenados por distancia ascendente"*. **"Libera en" es una estimación**, y hay
que decirlo así en la pantalla: no existe como dato, se deriva del tiempo esperado del
servicio actual de ese repartidor.

---

## Lo que NO entra

| Fuera | Por qué |
| :--- | :--- |
| **El mapa** | Es la dependencia más pesada (Leaflet + teselas + endpoint de flota en vivo). Su propia rebanada, después. |
| **"Asignación por IA"** | Se reemplaza por **sugerencia por cercanía y carga**: el más cercano libre, ordenados. Explicable, sin modelo. Es una desviación del mockup y queda marcada como tal. |
| **El valor del umbral de "no responde"** | Se define con la app móvil de domiciliarios. El filtro queda pronto y apagado. |
| **Cancelación en plena entrega** | Una comida ya recogida no se puede reasignar a otro domiciliario: hay que decidir si eso termina en `Cancelado` + incidencia, y eso necesita el modelo de incidencias que hoy no existe. |
| **Exportar la lista** | Es fácil pero no es el núcleo. |

---

## Verificación de cada rebanada

1. Tests del servicio en verde ⚠️ — los de Orders usan **PostGIS real en Testcontainers**
   (no hay base InMemory para las features), así que **Docker tiene que estar corriendo**:
   `dotnet test Deuna.Orders.Service.Tests/Deuna.Orders.Service.Tests.csproj`.
2. Comprobar que los tests muerden (mutación: romper el orden, el filtro de estado y el
   umbral) — si no fallan, no prueban nada.
3. `dotnet build` de la solución entera, no solo del proyecto tocado.
4. **Con datos reales**: pedidos sembrados vía API contra la base, y el endpoint
   consultado con token ADMIN y sin token (401) — igual que se verificó Metrics.
5. Si toca el gateway o el compose, la variable de entorno correspondiente en el
   compose: los destinos reales viven ahí, no en `appsettings.json`. ⚠️ B2 y B3 agregan
   rutas en Delivery, así que el gateway ya las enruta (`/api/v1/delivery/*`) pero
   conviene confirmarlo con una llamada real de punta a punta.
6. MR en GitHub, commits por unidad de trabajo, y checkpoint nuevo en Obsidian.

---

## Riesgos anotados

| Riesgo | Mitigación |
| :--- | :--- |
| Los rangos de prioridad terminan hardcodeados | Van en `appsettings.json` desde el primer commit, con los valores del negocio como configuración |
| El listado se vuelve lento con muchos pedidos | Índice por `(Estado, FechaCreacion)`; la paginación ya acota |
| "No responde" se define distinto en el backend y en la app móvil | El umbral es configuración, y su valor queda pendiente explícito, no inventado |
| La asignación manual y la automática se pisan | La manual valida contra la misma lista de candidatos y sobre el mismo estado; el evento es el mismo |
