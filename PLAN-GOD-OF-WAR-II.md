# God of War II — plan de integración con PCSX2

Estado: plan, sin implementación ni efectos nativos validados para II. Se amplía la rama `feat/god-of-war-cloud90` usada para I; no crear una rama o PR independiente por juego. Un PR combinado por repositorio: Game Link, backend y frontend. No fusionar ni desplegar durante esta fase.

Actualización: perfil II, identidad/firma, contexto, medidores de vida/magia/furia, escudo, catálogo, presets, permisos, instrucciones y portada PNG implementados en esta rama. El host es compartido y los datos son independientes. Mejoras, estados temporales y enemigos siguen pendientes. Ver `GOD-OF-WAR-II-VALIDATION.md` para evidencia y límites. El alcance original siguiente no afirma que todas sus fases estén terminadas.

## Evidencia local

- Imagen del usuario: `D:\PS2\Games\God of War II (USA).iso`, 8 533 671 936 bytes.
- Lectura directa de `SYSTEM.CNF` del disco: `BOOT2 = cdrom0:\SCUS_974.81;1`, `VER = 1.01`, `VMODE = NTSC`.
- El CRC del ejecutable, su huella, la identidad reportada por PINE y el comportamiento jugable quedan por confirmar. El nombre USA y el serial no bastan para autorizar escrituras.
- PCSX2 2.8.2 es la versión identificada previamente en este equipo. Volver a comprobarla al ejecutar II.
- El usuario realizará las pruebas manuales. Compilar y probar el protocolo automáticamente no demuestra efectos reales en el juego.

## Portada elegida

Reutilizar `gow-frontend/public/juegos/gow2.webp`, ya asignada a `gow2` en la web: imagen horizontal de Kratos de espaldas ante un templo, con ambas espadas encendidas. No usar una portada de God of War I, III, 2018 o Ragnarök.

Para Game Link convertir esa misma imagen a `assets/gow2.png`, sin recortar ni alterar la composición; incrustarla como `Art.gow2.png`, que ya admite el selector y el banner. Conservar el WebP en la web. La conversión y la integración visual de II son trabajo futuro de este plan, no una imagen ya añadida al cliente.

## Arquitectura

Reutilizar PINE, selección de PCSX2/ISO, WebSocket, tokens cifrados, reglas, historial, overlay, cancelación y permisos. Mantener `gow1` y `gow2` como juegos distintos con tokens/configuración separados.

Separar el transporte de la lógica actualmente concentrada en `GodOfWarHost`: host PCSX2 común, perfil GoW I y perfil GoW II. Cada perfil declara identidad exacta, firmas, contexto, límites, operaciones y capacidades. No basta con habilitar la pestaña II ni reutilizar las direcciones de I.

La conexión solo anuncia una capacidad cuando el perfil conoce sus direcciones/funciones y condiciones. Estado desconocido o revisión diferente: sin escrituras y sin partida confirmada. Confirmar aplicación tras la operación, distinguir resultados inciertos y nunca repetir automáticamente acciones acumulativas.

## Modificadores propuestos

| ID candidato | Efecto | Condición |
| --- | --- | --- |
| `heal` / `damage` | Recuperar 25 % / quitar 15 % de vida | Daño conserva 1 HP; máximo real actual |
| `heal_max` | Vida completa | Completa el medidor, no eleva su capacidad |
| `kill` | Matar a Kratos | Sin modo sin morir, permiso destructivo, cooldown mínimo de 600 s |
| `magic_add` / `magic_remove` | Recuperar / quitar 25 % de magia | Poder disponible y máximo real |
| `magic_max` / `magic_empty` | Magia completa / vacía | Vaciar una vez, sin bloqueo temporal implícito |
| `rage_max` / `rage_empty` | Furia de los Titanes completa / vacía | Habilidad disponible; escala y máximo propios de II |
| `blades_min` / `blades_max` | Espadas de Atenea al mínimo / máximo | Investigar niveles y actualización de recursos de II |
| `levels_min` / `levels_max` | Mejoras de armas/poderes al mínimo / máximo | Definir tabla propia; no confundir con dificultad/capítulo |
| `shield` | Escudo contra sabotajes de Cloud90 | No protege contra el daño normal del juego |
| `roulette` | Ruleta de efectos compatibles | Excluir por defecto muerte y cambios persistentes |
| `freeze` | Inmovilización temporal de Kratos | Fase posterior: identificar estado, entrada/salida y watchdog |
| `petrify` | Petrificación real | Fase posterior: estado del motor, recuperación y riesgo de muerte |

Los porcentajes son valores iniciales configurables tras comprobar la escala. Los niveles mínimos/máximos no desbloquearán armas o poderes de otra etapa de la historia sin una decisión y validación explícitas. Documentar si las mejoras persisten al guardar; no prometer reversión de escrituras instantáneas al pausar.

## Enemigos y dificultad avanzada

Investigar por separado: refuerzos compatibles con la zona, más vida/daño para enemigos activos y menor daño de Kratos. Primero localizar objetos, dueño, tipo, vida máxima, daño y ciclo de destrucción. Después comprobar una invocación del motor en su hilo de juego, con recursos ya cargados, posición válida y límite de enemigos extra. No copiar estructuras de memoria ni invocar jefes arbitrarios.

No anunciar esos efectos hasta demostrar que no bloquean el final del combate, puertas, cámaras o progreso. Respetar escudo y modo troll. Suspender en QTE, cinemáticas, menús, acertijos y escenas incompatibles. Determinar explícitamente qué escenas especiales o cambios de capacidades quedan fuera de la primera entrega; no asumir que todos los momentos de II usan al mismo Kratos y los mismos medidores.

## Fases y criterios de salida

1. **Identidad y base:** registrar versión PCSX2, serial, CRC y firmas. Confirmar arranque y controles. Respaldar configuración y memory cards antes de escrituras.
2. **Perfil de solo lectura:** mapear jugador, vida/máximos, magia, furia, niveles, pausas, cargas, cinemáticas, QTE, muerte y checkpoints. Repetir tras reinicio y en varias zonas.
3. **Primer efecto:** curación con límite correcto y confirmación; rechazo fuera de gameplay. Luego completar vida/magia y furia disponible.
4. **Muerte y mejoras:** aplicar permisos, cooldowns y pruebas de HUD, desbloqueos, armas equipadas y guardado. Añadir solo campos con actualización comprobada.
5. **Catálogo y cliente:** activar `gow2` en backend con versión mínima real; presets desactivados inicialmente, permisos y ruleta. Frontend: descarga real de la versión que incluya II, instrucciones, estados y filtros de capacidades. Game Link: perfil II y portada PNG. No reutilizar una descarga que solo soporte I.
6. **Estados temporales:** congelación/petrificación con duración máxima, salida normal, F9, escudo, muerte y restauración tras desconexión. Si el adaptador se cierra, el propio mecanismo del juego debe expirar el efecto. No simular congelación pausando PCSX2 entero.
7. **Enemigos/dificultad:** entregar después de mapear y comprobar el ciclo de objetos, con límites y capacidades por zona.
8. **QA y entrega:** tests del transporte/perfiles/políticas, tipos/lint, compilación completa conservando los módulos de otros juegos, comprobación de portada y regresiones GoW I. Preview identificable y luego pruebas manuales del usuario. No declarar estable por pasar solo tests simulados.

## Matriz mínima de pruebas manuales

- Gameplay normal, máximo de vida mejorado, magia/furia no disponibles y disponibles.
- Menú, pausa, cinemática, QTE, muerte, checkpoint, cambio de zona, cambio de arma y escenas especiales.
- Todos los modificadores por separado, límites, escudo, modo troll, modo sin morir y permiso destructivo.
- Evento simulado → regla → Game Link → PCSX2 → confirmación → historial/overlay; después eventos reales.
- Duplicados, expiración, cola, desconexión de red, cierre de cliente y de PCSX2, revisión equivocada y cambio de juego.
- Carga de estados: soportarla solo si se invalida el contexto de forma fiable; hasta entonces, documentar que exige pausar y reiniciar la conexión.
- Mejoras: HUD, cambio de recursos de arma, guardado/carga y ausencia de desbloqueos accidentales. No sobrescribir la partida principal durante pruebas.

## Alcance de esta actualización

Este documento añade el plan de II al mismo conjunto de cambios de I. No cambia la ISO, BIOS, memory cards o estados, ni habilita un perfil II, ni afirma que congelación, petrificación o enemigos estén implementados.
