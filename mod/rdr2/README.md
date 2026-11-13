# Diagnóstico de Red Dead Redemption 2

Primer paso de la integración de Game Link para la versión de Steam en Windows. El plugin usa las funciones nativas de Script Hook RDR2 para leer el contexto y la salud en modo Historia. La etiqueta `story_free_roam_candidate` señala que las comprobaciones disponibles coinciden con exploración libre. Su fiabilidad se debe comprobar en el juego antes de usarla para habilitar modificadores.

## Compilar

Extrae el SDK de [Script Hook RDR2](https://dev-c.com/rdr2/scripthookrdr2/) y ejecuta desde la raíz del repositorio con Visual Studio Build Tools y las herramientas C++ x64.

```powershell
.\mod\rdr2\build.ps1 -SdkPath 'C:\ruta\al\sdk'
```

El resultado es `mod\rdr2\dist\Cloud90.RDR2.Diagnostics.asi`.

Para compilar el cliente de prueba con las pestañas de RDR2 y God of War, usa el ejecutable publicado de Game Link 1.7.0-rc.1 como base de los módulos existentes.

```powershell
.\build-gow-preview.ps1 -BaseExecutable 'C:\ruta\Cloud90GameLink.exe' -Rdr2SdkPath 'C:\ruta\al\sdk'
```

## Probar con Cloud90 Live

1. Instala los componentes de Script Hook RDR2 compatibles y abre la compilación de Game Link para RDR2. Game Link instala únicamente su plugin propio.
2. Selecciona **Red Dead Redemption 2** en Game Link. Genera el token desde su ficha en Cloud90 Live y pégalo en Game Link. Pulsa **Abrir juego** y carga una partida de Historia en exploración libre.
3. Prueba Tónico de emergencia con la salud parcialmente vacía. Para probar Emboscada, desactiva Modo sin morir, elige un enemigo y verifica su aparición y combate. Para usarlo con regalos, activa Modo troll y guarda una regla con el regalo y el enemigo elegidos.
4. Prueba **Pausar efectos** desde Game Link o Cloud90 Live. El plugin cancela el escudo y limpia los enemigos de la sesión cuando vuelve a tener control. La conexión se suspende al salir de exploración libre.

Los atajos de la vista previa local quedan desactivados en una sesión iniciada por Game Link.

## Probar en modo Historia

1. Termina la instalación en Steam, abre el juego y completa el primer inicio con Rockstar Games Launcher. En Steam, usa **Propiedades → Archivos instalados → Explorar** y anota la versión de `RDR2.exe`.
2. Con el juego cerrado, instala el runtime y el cargador ASI x64 compatibles siguiendo la guía de Script Hook. Si la carpeta ya tiene un cargador, identifica cuál es antes de sustituir archivos. Coloca `Cloud90.RDR2.Diagnostics.asi` junto a `RDR2.exe`.
3. Abre el juego desde Steam en modo Historia. Carga una partida, camina en exploración libre, pausa y vuelve al juego, monta un caballo, y entra a una misión o cinemática. Comprueba el registro en `%LOCALAPPDATA%\Cloud90GameLink\rdr2\diagnostics.log`. El registro se renueva al iniciar el plugin y recoge los cambios observados dos veces por segundo mientras el hilo de Script Hook está activo.
4. Compara las entradas del registro con el estado real del juego, sobre todo durante menús, cinemáticas y cargas. Antes de usar Red Dead Online, cierra el juego y retira los componentes de modding. Para retirar únicamente el diagnóstico, borra `Cloud90.RDR2.Diagnostics.asi`.

## Probar los primeros modificadores

Con el juego cerrado, crea el archivo vacío `%LOCALAPPDATA%\Cloud90GameLink\rdr2\enable-preview.txt`. El plugin revisa ese archivo al iniciar. En exploración libre y con la ventana de RDR2 enfocada, usa los números de la fila superior:

| Atajo | Efecto | Qué verificar |
| --- | --- | --- |
| Ctrl + Shift + 1 | Tónico de emergencia | Recupera hasta el 25 % de la salud máxima. |
| Ctrl + Shift + 2 | Herida sorpresa | Quita hasta el 15 % de la salud máxima y conserva al menos 1 HP. |
| Ctrl + Shift + 3 | Puntería de forajido | Intenta recuperar hasta un cuarto del medidor de Dead Eye si está disponible e inactivo. La escala y respuesta del medidor están pendientes de confirmar en Steam. |
| Ctrl + Shift + 4 | Caballo fresco | Intenta recuperar hasta un cuarto de la resistencia máxima del caballo actual. Requiere estar montado. |
| Ctrl + Shift + 5 | Escudo del chat | Bloquea Herida sorpresa durante 15 segundos. Para probarlo, activa el escudo y luego pulsa el atajo de daño. |

El registro indica el efecto, el resultado y las lecturas antes y después cuando corresponda. `unconfirmed` significa que el juego no devolvió el valor esperado tras aplicar el efecto. Curación, Dead Eye y montura tienen 15 segundos de enfriamiento tras una aplicación confirmada o incierta. Daño tiene 30 segundos. El escudo bloquea los sabotajes de la vista previa y se cancela al detectar un cambio de personaje o al salir del contexto de exploración libre.

### Munición y persecución

Para probar estos tres efectos, crea también `%LOCALAPPDATA%\Cloud90GameLink\rdr2\enable-extended-preview.txt` antes de iniciar el juego. Usa una partida secundaria y registra los valores del HUD antes y después de cada prueba.

| Atajo | Efecto | Qué verificar |
| --- | --- | --- |
| Ctrl + Shift + 6 | Alforjas vacías | Quita hasta el 25 % de la reserva del tipo de munición del arma activa. Comprueba por separado el cargador y la reserva, también con munición especial. |
| Ctrl + Shift + 7 | Te busca la ley | Solicita una persecución local con un incidente sin recompensa explícita. Comprueba el aviso, la reacción de los agentes y la recompensa de la zona. |
| Ctrl + Shift + 8 | Pierde a la ley | Intenta terminar una persecución activa. Comprueba que la recompensa se conserve y que la persecución no reaparezca. |

Munición tiene 60 segundos de enfriamiento. Iniciar y terminar persecución tienen 90 segundos cada uno. La lectura inmediata del plugin puede marcar `unconfirmed` si el juego procesa el cambio en un fotograma posterior.

### Emboscada con enemigo seleccionable

Con los mismos dos archivos de activación, usa estos atajos en exploración libre. Cada opción crea un único enemigo e intenta dirigirlo contra el personaje controlado.

| Atajo | Enemigo | Modelo |
| --- | --- | --- |
| Ctrl + Shift + F1 | Bandido con revólver | `g_m_m_unibanditos_01` |
| Ctrl + Shift + F2 | Cazarrecompensas con carabina | `g_m_m_bountyhunters_01` |
| Ctrl + Shift + F3 | Lobo | `a_c_wolf` |
| Ctrl + Shift + F4 | Oso | `a_c_bear_01` |
| Ctrl + Shift + F5 | Vampiro de Saint Denis con daga | `cs_vampire` |
| Ctrl + Shift + F6 | Micah Bell | `cs_micahbell` |
| Ctrl + Shift + F7 | Cavernícola | `a_m_m_unicorpse_01`, variante 45 |
| Ctrl + Shift + F8 | Colm O’Driscoll | `cs_colmodriscoll` |
| Ctrl + Shift + F9 | Andrew Milton | `cs_miltonandrews` |
| Ctrl + Shift + F10 | Ermitaño de la cueva | `cs_cavehermit` |
| Ctrl + Shift + F11 | Puma | `a_c_cougar_01` |
| Ctrl + Shift + F12 | Caimán, cerca de agua | `a_c_alligator_01` |

Con Game Link Preview 3, una emboscada por regalo muestra el usuario del remitente sobre el enemigo visible. El nombre visible sirve de respaldo. Los espectadores silenciados u ocultos aparecen como «Alguien». En modo Historia, verifica el nombre con un regalo real y que desaparezca al morir el enemigo o cerrar la emboscada. Comprueba la apariencia del cavernícola con la variante 45 y el ataque de cada enemigo nuevo.

La emboscada busca un punto con terreno cargado a 12–26 metros del jugador. Tiene un enfriamiento compartido de 60 segundos desde que acepta cargar el modelo y dura hasta 45 segundos. Intenta retirar al NPC propio si muere, cambia el personaje, se pierde el contexto o termina el tiempo. Si el juego pausa el hilo del plugin, la limpieza se procesa cuando vuelve a ejecutarse.

Revisa las entradas `effect=bandit_ambush` del registro. `spawned` confirma la creación del NPC. `combat_confirmed` confirma que el juego detectó combate con el jugador. Prueba cada modelo en terreno abierto, revisa que el vampiro use la daga y verifica la limpieza al pausar, morir o entrar en una misión. El efecto reutiliza el modelo del vampiro y le asigna combate local.

Estos atajos son locales. Quita el archivo `enable-preview.txt` y reinicia el juego para volver al diagnóstico de solo lectura. La tormenta y el caballo de rescate siguen pendientes.

## Referencias

- [API y runtime de Script Hook RDR2](https://dev-c.com/rdr2/scripthookrdr2/)
- [Base de funciones nativas de RDR3](https://github.com/alloc8or/rdr3-nativedb-data)
- [Funciones de persecución documentadas por el autor de RDR2 Trainer](https://github.com/jonas-lundahl/rdr2-trainer/blob/main/docs/wanted-sdk.md)
- [Modelos de personajes y animales del juego](https://github.com/femga/rdr3_discoveries/blob/master/peds/peds_list.lua)
- [Identificadores de armas del juego](https://github.com/femga/rdr3_discoveries/blob/master/weapons/weapons.lua)
