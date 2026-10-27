# Investigación: más efectos troll para Cuphead

Fecha: 2026-10-27. Estado: **los doce candidatos están implementados en los mismos PR; su validación visual dentro del juego sigue pendiente por decisión del usuario**.

Se revisó la instalación Steam x64 build 20241530 que ya usamos para las pruebas,
el plugin Cloud90 existente y fuentes públicas de otros mods. Los resultados de
análisis estático no equivalen a una prueba real. Este documento amplía el diseño
en el mismo PR #5. La ampliación ya agrega los doce efectos al catálogo y a
«Probar un efecto», conservando las reglas que el usuario tenga configuradas.
Los apartados siguientes conservan la investigación original; los límites finales
y el estado de pruebas están en [README.md](README.md) y [VALIDATION.md](VALIDATION.md).
Petrificación quedó limitada a avión porque solo ese controlador se suscribe al
evento nativo; refuerzos requieren un spawner permitido de Run & Gun.

## Evidencia principal

- El motor terrestre separa movimiento, dash, golpe y fuerzas externas. Se
  identificaron `LevelPlayerMotor.HandleWalking`, `VelocityManager.move`,
  `AddForce` y `RemoveForce`. Permiten investigar inercia y empujones sin cambiar
  la geometría ni la fricción de todos los objetos del escenario.
- Hay una ruta nativa de petrificación: `PlayerStatsManager.GetStoned` y
  `StoneTime`. El juego permite reducir su duración mediante entradas del jugador;
  no debe anunciarse como una inmovilización de duración fija sin probarlo.
- `SceneLoader.ReloadLevel` existe en esta build. Tiene ramas especiales para
  Tower of Power, King Dice, ajedrez y cementerio; algunas modifican contadores o
  guardan datos. Reutilizar nuestra exclusión de esos contextos es obligatorio.
- El daño normal de `PlayerStatsManager` resta un HP por golpe. Un `DamageInfo`
  con un número enorme no demuestra que se pueda matar de un solo golpe.
- `OnStatsDeath` completa eventos, audio y contador de muertes del juego. Cambiar
  solamente `Health` a cero no ejecuta esa secuencia. El efecto de muerte tendría
  consecuencias normales de una muerte, incluido el contador correspondiente.
- No se identificó un estado universal de quemadura reutilizable en el gestor
  de estadísticas examinado. Quemadura sería un efecto propio del plugin con
  temporizador, visuales y golpes limitados, no una función nativa ya confirmada.
- `EnemyDatabase` devuelve propiedades de enemigos, no un catálogo universal
  de prefabs. Los spawners de plataformas guardan un `enemyPrefab`; por ejemplo,
  el spawner terrestre genera ese prefab y lo coloca usando la cámara del nivel.
  Esto respalda un proveedor por nivel, no generar cualquier enemigo en cualquier
  pelea. Jefes, proyectiles, animadores y referencias de escena requieren pruebas.

## Candidatos

Las duraciones y enfriamientos son valores iniciales propuestos, no límites
ya publicados ni promesas de compatibilidad.

| Efecto propuesto | Comportamiento | Ruta / viabilidad | Alcance inicial y prueba clave |
|---|---|---|---|
| `ice_floor` — Piso de hielo | Acelera y frena con inercia durante 5–8 s | Desarrollo propio sobre movimiento; viabilidad media | Tierra. Comprobar frenar, cambiar dirección, bordes, plataformas, dash y restauración; no convertir todo el escenario en hielo |
| `wind_push` — Ráfaga de viento | Empuje breve a izquierda o derecha | Hay fuerzas externas en el motor; viabilidad alta para prototipo | Tierra primero. Limitar fuerza y duración, conservar colisiones y retirar solo la fuerza creada por Cloud90 |
| `explosion` — Explosión | Efecto visual, un golpe de 1 HP y empuje opcional | Composición propia; viabilidad media | No prometer una explosión nativa universal ni daño de área. Respetar protección, escudo y última vida |
| `burn` — Quemadura | Visual de fuego y hasta dos intentos de daño espaciados | Efecto propio; viabilidad media | Probar intervalo contra invulnerabilidad nativa. Revisar la última vida en cada golpe. F9 cancela lo que falte; no quitar protecciones para forzar daño |
| `restart_level` — Reiniciar nivel | Repetir el intento actual desde su inicio | Ruta nativa localizada y precedente público; viabilidad alta | Batallas normales y Run & Gun. Excluir modos especiales y transiciones; no volver al inicio del guardado |
| `kill` — Muerte | Terminar el intento con la muerte normal del personaje | Flujo de muerte identificado y precedente público; viabilidad alta para prototipo | Solo con permiso destructivo y Modo sin morir desactivado. Probar pantalla de muerte y Retry; no usar solamente SetHealth(0) |
| `spawn_enemies` — Refuerzos enemigos | Aparecen uno o dos enemigos adicionales | Depende del nivel y de prefabs cargados; complejidad alta | Empezar por un Run & Gun y uno o dos prefabs concretos. No clonar jefes ni cargar recursos de otras peleas |
| `petrify` — Petrificación | Usar el estado nativo de piedra | API localizada; restauración y personajes pendientes | Probar Cuphead, Ms. Chalice y avión por separado. El jugador puede reducir el tiempo con entradas |
| `super_drain` — Vaciar súper | Quitar cartas de súper del intento actual | Estado y actualización del HUD identificados; viabilidad alta | Respetar cambios de personaje, límites y supers ya en curso; no modificar equipamiento o progreso |
| `auto_jump` — Saltos involuntarios | Saltos breves separados, solo al estar en suelo | Adaptación de entrada; viabilidad media | No confundirlo con parry o doble salto de Chalice. Probar bordes y bloqueo simultáneo de salto |
| `weak_shots` — Disparos débiles | Reducir temporalmente el daño de los disparos del jugador | Candidato adicional; falta revisar el adaptador de daño por propietario | No cambiar globalmente el daño de enemigos ni la vida del jefe; tierra y avión separados |
| `camera_zoom` — Cámara incómoda | Zoom moderado y temporal | Existe precedente público; viabilidad media | Reversible, HUD legible, sin alterar límites/colliders. Evitar zoom extremo y destellos |

## Qué recomiendo implementar primero

1. **Primer bloque:** piso de hielo, ráfaga de viento y vaciar súper. Son troleos
   repetibles que no necesitan enemigos de otra escena ni reiniciar el juego.
   Hielo necesita más desarrollo que una simple reducción de velocidad.
2. **Segundo bloque:** explosión controlada y quemadura. Definir su presentación
   visual y daño antes de publicarlas; probar protección nativa y cancelación
   de todos los golpes futuros. No prometer un estado nativo de fuego.
3. **Bloque destructivo:** reiniciar nivel y muerte. Mantenerlo apagado por
   defecto, exigir habilitación específica, respetar Modo sin morir y escudo y
   excluirlo de la ruleta predeterminada. Propuesta: 180–300 s para reinicio y
   600 s para muerte, además del límite global existente.
   Recomiendo un aviso/cuenta atrás de 2–3 s y volver a comprobar contexto,
   escudo y permisos justo antes de ejecutar. F9 puede cancelar esa orden
   pendiente; no puede deshacer una muerte o reinicio que ya se ejecutó.
4. **Prototipo de enemigos separado dentro del mismo PR:** un Run & Gun concreto,
   un prefab del spawner ya cargado, máximo dos o tres refuerzos simultáneos y
   limpieza al cambiar de intento. Probar posición válida, ciclo de vida,
   rendimiento y posibles cambios en contadores de enemigos/puntuación antes
   de habilitar otro nivel. Si no hay proveedor compatible, rechazar con motivo.

Petrificación, saltos involuntarios, disparos débiles y cámara quedarían como
siguiente selección, después de validar los anteriores. No recomiendo gravedad
invertida ni teletransportar al punto inicial para esta ampliación: hay rutas
parciales en el motor, pero dependen de colisiones, cámara y reglas de cada nivel.

## Integración en los mismos tres PR

- **Game Link #5:** implementar los adaptadores, conservar el epoch por intento y
  la cancelación F9. Para enemigos, exponer capacidades del contexto actual en el
  estado del plugin, en lugar de anunciar compatibilidad universal. Mantener
  referencias a los objetos creados y retirar únicamente esos objetos/modificadores.
- **Backend #96:** agregar solo los efectos aprobados, con troll/lethal, límites,
  enfriamientos y exclusión de la ruleta inicial cuando corresponda. Filtrar
  eventos/ruleta según capacidades del nivel y mantener los rechazos en historial.
  El escudo debe cancelar o impedir golpes/creaciones pendientes de efectos troll;
  no basta con revisar el escudo solo al recibir una orden de quemadura.
- **Frontend #133:** nombres claros y pasos breves, opciones para efectos
  destructivos y estado “No compatible con este nivel”. Diferenciar Explosión de
  Muerte y Reiniciar nivel de teletransporte. Mantener controles accesibles.
- **Pruebas por bloque:** Cuphead, Ms. Chalice, Run & Gun, jefe base, Saltbaker y
  avión cuando corresponda; además pausa, F9, escudo, última vida, Retry,
  desconexión, orden duplicada y comando retrasado de otro intento. No incluir
  un efecto en el catálogo público hasta superar su matriz aplicable.

La configuración destructiva adicional y el filtrado por capacidades son propuestas
de diseño. Todavía no existen como opciones nuevas en los tres PR.

## Fuentes consultadas

Se inspeccionaron métodos de la copia local del juego; no se incorpora el código
descompilado ni recursos del juego a este repositorio.

Fuentes públicas primarias, consultadas el 2026-10-27:

- [Cuphead.DebugMod — README](https://github.com/DemoJameson/Cuphead.DebugMod):
  precedente de Retry, velocidad global y cámara.
- [Cuphead.DebugMod — Misc.cs](https://github.com/DemoJameson/Cuphead.DebugMod/blob/a03a8e083acd7aa7276b5b3c0c418d1326e1ead1/Cuphead.DebugMod/Components/Misc.cs):
  implementación pública de Quick Retry y manejo especial de King Dice.
- [Cuphead.DebugMod — CameraZoom.cs](https://github.com/DemoJameson/Cuphead.DebugMod/blob/a03a8e083acd7aa7276b5b3c0c418d1326e1ead1/Cuphead.DebugMod/Components/CameraZoom.cs):
  referencia de zoom y restauración por escena.
- [CupheadArchipelago — PlayerStatsInterface.cs](https://github.com/JKLeckr/CupheadArchipelagoMod/blob/c21a9d16138063e76818dce763047daa234313dc/CupheadArchipelago/Unity/PlayerStatsInterface.cs):
  interfaz pública de salud, súper y muerte.
- [CupheadArchipelago — PlayerStatsManagerHook.cs](https://github.com/JKLeckr/CupheadArchipelagoMod/blob/c21a9d16138063e76818dce763047daa234313dc/CupheadArchipelago/Hooks/PlayerHooks/PlayerStatsManagerHook.cs):
  precedente de ejecutar el flujo completo de muerte.

Commits de referencia observados: DebugMod `a03a8e083acd7aa7276b5b3c0c418d1326e1ead1`;
Archipelago `c21a9d16138063e76818dce763047daa234313dc`. Son precedentes de
implementación, no una garantía de compatibilidad con Cloud90 ni una dependencia
que se vaya a instalar en las máquinas de los usuarios.
