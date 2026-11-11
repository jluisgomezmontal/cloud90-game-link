# Celeste en Cloud90 Game Link

Vista previa para Celeste de Steam en Windows y los capítulos del juego base. Instala [Everest 1.6662.0](https://github.com/EverestAPI/Everest/releases/tag/stable-1.6662.0) con [Olympus](https://everestapi.github.io/) en la carpeta de Steam. Game Link instala únicamente su ZIP en Mods.

## Conexión

1. Abre la pestaña Celeste de Game Link, selecciona la carpeta que contiene Celeste.exe y pega el token de la ficha de Celeste en Cloud90 Live.
2. Abre el juego desde Game Link, entra a una sala controlable y prueba Recargar dash con una carga gastada.
3. Para los sabotajes por regalo, habilita la integración y Modo troll. Desactiva Modo sin morir si quieres probar perseguidor o reinicio. El reinicio también requiere su permiso específico.
4. Pulsa F9 con Celeste enfocado o usa el control de pausa de Game Link para detener los efectos.

| Efecto | Comportamiento y prueba |
|---|---|
| Recargar dash | Respeta el máximo de Madeline y las reglas de recarga del juego. |
| Recuperar resistencia | Recupera el medidor de escalada. Pruébalo tras agotar parte de la resistencia. |
| Escudo del chat | Bloquea sabotajes de Cloud90 hasta 12 segundos. |
| Perder un dash | Quita una carga en suelo y bloquea la recarga automática en suelo hasta 1,25 segundos. |
| Viento lateral | Empuja a izquierda o derecha hasta 4 segundos en salas compatibles sin viento nativo activo. |
| Dash corto | Arma una reducción de velocidad para el siguiente dash dentro de una ventana de 8 segundos. |
| Controles invertidos | Invierte el eje horizontal hasta 4 segundos. Prueba teclado y mando. |
| Reiniciar sala | Solicita una muerte normal de Madeline. Respeta la protección propia del juego. |
| Invocar perseguidor | Buscador en los capítulos 2 y 5, o Badeline en el capítulo 2 si no hay otra Badeline. Busca espacio libre y retira la entidad propia al expirar o cambiar de contexto. |
| Muelle de rescate | Crea un muelle temporal si hay espacio y suelo sólido cerca de Madeline. |

La vista previa limita viento, dash corto, controles, pérdida de dash y muelle a los capítulos 1–5, lado A. Valida transición, muerte, pausa, expiración y desconexión en cada efecto. Verifica si los perseguidores atacan en la sala elegida. La confirmación de una entidad indica que el juego la añadió a la escena.

## Compilar

Descarga `lib-stripped.zip` del release de Everest indicado y extrae su carpeta `lib-stripped`. Con .NET 8 SDK, ejecuta desde el repositorio:

```powershell
.\mod\celeste\build.ps1 -EverestLib 'C:\ruta\lib-stripped'
.\build-gow-preview.ps1 -BaseExecutable 'C:\ruta\GameLink-Preview1.exe' -CelesteEverestLib 'C:\ruta\lib-stripped'
```

La segunda línea empaqueta el cliente con los recursos de los juegos anteriores a partir de Game Link 1.8.0 Preview 1. El ZIP de Celeste incluye el ensamblado propio y su manifiesto de Everest.
