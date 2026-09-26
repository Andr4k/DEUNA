# Plan — Pantalla "Servicios finalizados"

Historial de servicios completados, con KPIs, filtros, tabla y exportación.
Estado: **propuesta**. No hay código escrito todavía.

---

## 1. Qué pide la pantalla

**KPIs:** servicios completados hoy, valor domicilios hoy, calificación promedio, con incidencias,
tiempo promedio de entrega. Los cuatro primeros con comparación contra ayer.

**Filtros:** rango de fechas, restaurante, domiciliario, zona, calificación, incidencias, pago,
y búsqueda de texto libre (pedido, cliente o dirección). Más botón de exportar.

**Tabla:** pedido y hora, restaurante (nombre + ciudad), domiciliario (nombre + su calificación),
origen → destino, los tiempos del servicio (asignado / recogido / entregado + duración total),
valor y quién paga, dos calificaciones (domiciliario y restaurante), estado, y "ver detalle".

---

## 2. Qué existe y qué falta (medido en las bases, no supuesto)

| Dato | Origen | Estado |
| :--- | :--- | :--- |
| Pedido, código, estado, montos | **Orders** `pedidos` | Existe. 20 en `Entregado` |
| Fechas del ciclo | **Orders** `pedidos`: `FechaCreacion`, `FechaConfirmacion`, `FechaPreparacion`, `FechaListo`, `FechaEntrega` | Existe |
| Origen (restaurante) | **Orders** `restaurantes_replicados`: `NombreComercial`, `DireccionSede`, `Ciudad` | Existe |
| Destino y zona | **Orders** `direcciones_entrega` + `pedidos.DireccionEntregaId` | Existe |
| Domiciliario asignado | **Delivery** `asignaciones_repartidor`: `PedidoId`, `RepartidorId`, `Estado`, `DistanciaMetros` | Existe |
| Asignación y llegada | **Delivery** `asignaciones_repartidor`: `FechaAsignacion`, `FechaLlegadaLocal` | Existe |
| Nombre del domiciliario | **Delivery** `repartidores_replicados` | Existe |
| Calificaciones | **Feedback** `feedback_encuestas` + `feedback_detalle_criterios` (criterios en FILAS, por TASK-402) | Existe, **a confirmar qué criterio corresponde al domiciliario y cuál al restaurante** |
| **Incidencias** | — | **NO EXISTE en ningún servicio.** Verificado en las tres bases |
| "Quién paga" (Cliente / Domiciliario) | A confirmar en `tarifas_aplicadas` | **A confirmar** |
| Exportar | — | No existe. Es nuevo |

**Dos bloqueos reales:** la columna "Incidencias" y el KPI "con incidencias" **no tienen fuente**.
El KPI "calificación promedio" del mockup dice "basado en 38 calificaciones" y hay 20 entregados:
los números del mockup son de diseño, no de datos.

---

## 3. Quién arma la respuesta (decisión de arquitectura)

Los datos viven en **tres servicios** (Orders, Delivery, Feedback) y el portal solo habla con el gateway.

**La pantalla NO debe consultar tres endpoints y cruzar en el navegador**, y **Orders no debe llamar a
Delivery** en un camino de lectura: eso crea un acoplamiento entre servicios que hoy no existe.

**Va en `Deuna.Orders.Service`**, porque **Orders es el dueño del pedido y esta pantalla es el historial de
pedidos**: el restaurante, el destino, los montos y todas las fechas del ciclo ya viven ahí.

**El domiciliario, sus tiempos y las calificaciones llegan por RÉPLICA alimentada por eventos** — el patrón
que el proyecto ya usa en todas partes: Delivery mantiene `pedidos_disponibles`, `repartidores_replicados` y
`restaurantes_replicados`; Orders mantiene `restaurantes_replicados`; Feedback mantiene `pedidos_replicados`.
Ningún servicio le pide datos a otro en una lectura: cada uno proyecta lo que necesita. Esta pantalla sigue
esa misma disciplina.

**No va en `Deuna.Metrics.Service`**: ese servicio provee datos derivados —conteos, promedios, rendimiento— y
devolver un listado paginado de entidades de negocio con su detalle es otra responsabilidad.

**Tampoco va en `Deuna.Shared`**: Shared es una librería de contratos que se compila dentro de cada servicio
(no tiene `Program.cs` ni host propio), así que no puede exponer un endpoint. Sí es el lugar correcto para
**los eventos nuevos** que alimenten la réplica.

Un endpoint nuevo en Orders:

```
GET /api/v1/admin/orders/servicios-finalizados
    ?desde=&hasta=&restauranteId=&repartidorId=&zona=&calificacionMin=
    &conIncidencia=&pago=&buscar=&pagina=&tamano=
```

Rol **ADMIN**, por el gateway. **Ojo con el prefijo**: el grupo `/api/v1/orders` exige rol `restaurant`, así
que esta ruta necesita su propio grupo — igual que `/api/v1/admin/orders/sin-asignar`, que ya lo tiene.

### 3.1 Lo que hay que agregar para que la réplica funcione

- Una tabla de réplica en Orders con el domiciliario asignado y los tiempos del ciclo (una migración).
- Un consumidor de `PedidoAsignado` (el evento ya existe) para llenarla.
- **Un evento de calificación registrada**: hay que verificar si Feedback ya lo publica. Si no existe, es un
  contrato nuevo en `Deuna.Shared/Events/IntegrationEvents.cs` más su consumidor en Orders.

---

## 4. Las consultas agregadas

El cruce se hace **por `PedidoId`**, y ahí está la trampa de nombres que hay que dejar escrita:

> En **Orders** la clave del pedido se llama `Id`. En **Delivery** la misma entidad se llama `PedidoId`
> en `asignaciones_repartidor` y en `pedidos_disponibles`. **Son el mismo valor.** Cualquier código que
> las trate como cosas distintas va a producir cruces vacíos silenciosos.

### 4.1 Los servicios del rango (Orders)

```sql
select p."Id" as pedidoId, p."Codigo", p."Estado", p."Total", p."CostoEnvio",
       p."FechaCreacion", p."FechaConfirmacion", p."FechaListo", p."FechaEntrega",
       r."NombreComercial" as restaurante, r."DireccionSede" as origen, r."Ciudad",
       d."Direccion" as destino, d."Ciudad" as zona
from pedidos p
join restaurantes_replicados r on r."Id" = p."RestauranteId"
join direcciones_entrega    d on d."Id" = p."DireccionEntregaId"
where p."Estado" = 'Entregado'
  and p."FechaEntrega" >= @desde and p."FechaEntrega" < @hasta
order by p."FechaEntrega" desc;
```

### 4.2 Los domiciliarios y los tiempos del ciclo (Delivery)

```sql
select a."PedidoId", a."RepartidorId", a."Estado", a."DistanciaMetros",
       a."FechaAsignacion", a."FechaLlegadaLocal",
       rep."NombreCompleto"
from asignaciones_repartidor a
left join repartidores_replicados rep on rep."Id" = a."RepartidorId"
where a."PedidoId" = any(@pedidoIds);
```

### 4.3 Las calificaciones (Feedback)

Por pedido, separando el criterio del domiciliario del criterio del restaurante. **Los criterios están en
filas** (`feedback_detalle_criterios`), no en columnas: hay que confirmar el nombre exacto de cada criterio
antes de escribir la consulta, y si no se distingue por criterio, el promedio por sujeto **no se puede
calcular** y hay que decirlo en vez de inventarlo.

### 4.4 Los KPIs

Se calculan sobre el conjunto ya cruzado, no con consultas separadas: así los números de arriba y las
filas de abajo **no pueden discrepar**. Es el mismo error que ya evitamos en el panel principal —un
contador que decía 12 y una lista que mostraba otra cosa—.

Las variaciones contra ayer se calculan corriendo el mismo agregado con el rango de ayer.

---

## 5. Los contratos (esta sección es la que ahorra tiempo)

Reglas que aplican a todo lo de abajo:
- **Nombres en español**, igual que el resto de los DTOs del proyecto.
- **Fechas como ISO 8601 en string**, nunca como número.
- **Duraciones calculadas en el backend**, en minutos, como número. El cliente no resta fechas: si el
  cálculo vive en dos lados, tarde o temprano difieren.
- **`| null` explícito** en todo lo que puede faltar, y el comentario dice por qué puede faltar.
- **Los estados son los canónicos** de `Deuna.Shared.Domain.EstadosPedido` y de `EstadoAsignacion`.
  No se inventa un literal nuevo para la UI.
- **Los agregados que no se pueden calcular viajan en `null`**, no en `0`. Un promedio de cero y un
  promedio que no existe se ven igual en pantalla y significan cosas opuestas.

### 5.1 El sobre de la página

```ts
interface PaginaServiciosFinalizados {
  items: ServicioFinalizado[];
  total: number;
  pagina: number;
  tamano: number;
}
```

### 5.2 La fila

```ts
interface ServicioFinalizado {
  pedidoId: string;
  codigo: string;                       // "PED-20260923-000001"

  cerradoEn: string;                    // ISO — FechaEntrega
  restaurante: {
    id: string;
    nombre: string;
    ciudad: string;
  };
  domiciliario: {
    id: string;
    nombre: string;
    calificacion: number | null;        // promedio histórico del domiciliario; null si nunca lo calificaron
  } | null;                             // null si el pedido se entregó sin asignación registrada

  origen:  { direccion: string };       // la sede del restaurante
  destino: { direccion: string; zona: string };

  tiempos: {
    asignadoEn:  string | null;         // ISO
    recogidoEn:  string | null;
    entregadoEn: string;                // ISO — siempre existe si el servicio está cerrado
    minutosTotales: number | null;      // null si falta algún extremo del cálculo
  };

  valor: {
    domicilio: number;
    total: number;
    paga: "Cliente" | "Domiciliario" | null;   // null hasta confirmar de dónde sale
  };

  calificaciones: {
    domiciliario: number | null;        // 1..5
    restaurante:  number | null;
  };

  incidencia: {
    hay: boolean;
    motivo: string | null;
  };                                    // hoy siempre { hay: false, motivo: null } hasta que exista el modelo

  estado: EstadoPedido;                 // canónico
}
```

### 5.3 Los KPIs

```ts
interface ResumenServiciosFinalizados {
  completadosHoy: number;
  valorDomiciliosHoy: number;
  calificacionPromedio: number | null;  // null si no hay ninguna calificación en el rango
  calificacionesContadas: number;       // el "basado en N calificaciones" del mockup
  conIncidencias: number | null;        // null mientras no exista el modelo de incidencias
  tiempoPromedioMin: number | null;
  ayer: {
    completados: number;
    valorDomicilios: number;
    tiempoPromedioMin: number | null;   // para la variación porcentual
  };
}
```

---

## 6. Qué falta decidir antes de escribir código

1. **Incidencias**: ¿qué es una incidencia y quién la crea? Sin modelo no hay columna, no hay filtro y no
   hay KPI. Es una rebanada propia y hoy no existe en ningún servicio.
2. **"Quién paga"**: confirmar si sale de `tarifas_aplicadas` o si es un dato que no existe.
3. **Los criterios del feedback**: confirmar en `feedback_detalle_criterios` cuál es el del domiciliario y
   cuál el del restaurante. Si no se distinguen, la calificación por sujeto no es calculable.
4. **Exportar**: ¿CSV? ¿Y exporta la página visible o todo el filtro?

---

## 7. Plan de implementación

Cada rebanada es un MR propio, con su rama y sus commits atómicos. **El orden no es estético: cada una
habilita la siguiente.** Las diferibles están marcadas.

### Rebanada 1 — Los contratos, sin lógica

**Qué:** los DTOs en `Deuna.Orders.Service/DTOs/` y los tipos en `src/lib/tipos/servicios-finalizados.ts`.
**Por qué primero y sola:** es la que evita el tiempo perdido en errores de contrato. Las dos puntas se
escriben contra el mismo documento y ningún ajuste de nombres aparece a mitad de camino.
**Cómo se verifica:** compila, y los tipos coinciden campo por campo. Nada más.

### Rebanada 2 — La réplica en Orders

**Qué:** una tabla nueva en Orders con el domiciliario asignado y los tiempos del ciclo; su migración; y un
consumidor de `PedidoAsignado` (el evento ya existe).
**Por qué:** es lo que hace que la pantalla sea una consulta de **un solo servicio**.
**Cómo se verifica:** TDD. Un `PedidoAsignado` consumido deja la fila; un duplicado no la duplica. Y la
prueba de que el test muerde: si el consumidor no escribe, el test tiene que caer.

### Rebanada 3 — El evento de calificación · DIFERIBLE

**Qué:** verificar si Feedback ya publica un evento al calificar. Si no existe: contrato nuevo en
`Deuna.Shared/Events/IntegrationEvents.cs`, publicación en Feedback, consumidor en Orders.
**Si se difiere:** la columna de calificaciones sale en `null` y la pantalla lo dice. El contrato ya lo
contempla, así que diferirla no rompe nada.

### Rebanada 4 — El endpoint

**Qué:** `GET /api/v1/admin/orders/servicios-finalizados` con filtros, paginación y KPIs, en su propio grupo
de política `admin`. Más la ruta en el gateway.
**Cómo se verifica:** TDD, y hay un test que importa más que los otros: **que los KPIs y las filas salgan del
mismo conjunto**. Un contador que dice 42 mientras la lista muestra 38 es el bug que ya tuvimos en el panel
principal, y este test es el que lo previene.

### Rebanada 5 — La pantalla

**Qué:** KPIs, filtros y tabla en el portal, con las primitivas de la pantalla de Pedidos.
**Cómo se verifica:** renderizada y capturada. Sin imagen no está verificada.

### Rebanada 6 — El detalle del servicio · DIFERIBLE

**Qué:** la vista de "Ver detalle".

### Rebanada 7 — Exportar · DIFERIBLE

**Qué:** depende de la decisión 4 (¿la página visible o todo el filtro?).

### Bloqueada, no planificada

**Incidencias.** Sin modelo no hay columna, filtro ni KPI. Es su propia rebanada y necesita una decisión de
producto antes: qué es una incidencia y quién la crea.

---

## 8. Dependencias

```
1 contratos ──┬─> 2 réplica ──┐
              │               ├─> 4 endpoint ──> 5 pantalla ──> 6 detalle
              └─> 3 calificación (diferible) ──┘

7 exportar   (independiente, espera decisión)
8 incidencias (bloqueada, espera modelo)
```

La 1 habilita a la 2, la 3 y la 4. La 3 es la única que se puede diferir sin dejar nada roto, porque el
contrato ya declara sus campos como `null`.
