# Black Myth: Wukong en Cloud90 Game Link

Primera entrega para la edición individual de Steam en Windows, App ID `2358720`. Reutilizar la ficha `wukong` de «Próximamente», su portada y la categoría Acción y aventura. El build exacto de Steam queda por identificar antes de fijar compatibilidad. [Ficha de Steam](https://store.steampowered.com/app/2358720/Black_Myth_Wukong/)

## Ruta técnica

1. Detectar la instalación con `appmanifest_2358720.acf` y ofrecer selección manual de la carpeta con `b1`. Registrar el build y la versión del ejecutable del juego. Revisar qué cargadores están presentes antes de instalar.
2. Probar un módulo propio con UE4SS. Usar un diagnóstico de solo lectura para localizar al Destinado, distinguir exploración, combate, santuario, cinemática, muerte y transformación, y leer vida, maná, resistencia y concentración. Ejecutar las operaciones de Unreal en el hilo del juego. Comparar con una extensión C# solo si el diagnóstico muestra funciones necesarias que la ruta Lua no expone. [UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) · [WukongGM](https://github.com/lazyassin/WukongGM)
3. Implementar curación como primer recorrido completo. Game Link instala el módulo propio y conserva los cargadores y mods ajenos. El cliente mantiene el token y usa el buzón local con latido, contexto, caducidad, confirmación e IDs únicos. Un regalo debe cambiar la barra y aparecer en historial y overlay después de la confirmación.

## Modificadores

Los tiempos y valores son propuestas para probar en el build instalado.

| Tanda | Efecto | Comportamiento | Enfriamiento inicial |
| --- | --- | --- | --- |
| Primera | Curación | Recupera hasta 25 % de la vida máxima efectiva. | 15 s |
| Primera | Vida máxima | Rellena la barra del Destinado. | 45 s |
| Primera | Herida | Quita hasta 20 % de la vida máxima con piso de 1 HP. | 30 s |
| Primera | Recuperar o vaciar maná | Recarga hasta 30 % o vacía el medidor. Rechaza durante el lanzamiento de un hechizo. | 30 / 45 s |
| Primera | Recuperar resistencia | Recarga hasta 35 % de la resistencia efectiva. | 20 s |
| Primera | Dar o quitar concentración | Añade o quita una carga dentro de los límites actuales. Comprueba consumo y HUD. | 30 s |
| Primera | Escudo del chat | Bloquea sabotajes de Cloud90 durante 15 s. | 30 s |
| Segunda | Añadir o quitar un sorbo | Cambia una carga de la calabaza sin modificar mejoras. Rechaza durante la animación de beber. | 45 / 60 s |
| Segunda | Recargar Qi | Recupera hasta 25 % del medidor de espíritu o artefacto, según la opción de la regla. | 60 s |
| Segunda | Acortar enfriamiento de un hechizo | Reduce el enfriamiento actual de un hechizo equipado. Validar Inmovilización como primer candidato. | 60 s |
| Segunda | Bastón bendecido o debilitado | Aplica ±25 % de daño del bastón durante 10 s. | 45 s |
| Segunda | Paso pesado o bloquear esquiva | Reduce velocidad durante 4 s o impide iniciar esquivas durante 3 s. Solo en suelo y estados compatibles. | 45 s |
| Segunda | Invulnerabilidad breve | Protección contra golpes de combate durante 5 s. Verificar el alcance por tipo de daño. | 60 s |
| Segunda | Derrotar al Destinado | Solicita la muerte normal. Requiere permiso y desactivar Modo sin morir. | 120 s |
| Investigación | Energía de transformación | Recuperar o reducir la energía únicamente tras validar cada forma y su transición. | Por medir |

## Emboscada

Un regalo selecciona una criatura y el módulo la invoca en terreno transitable, con espacio libre y lejos del santuario o de una arena en curso. Usar una lista de clases aprobadas por build. La existencia de mods de invocación demuestra viabilidad general, pero cada variante requiere probar IA, animaciones y limpieza en el build de Steam. [Give Me Everything, publicado por su autor](https://www.nexusmods.com/blackmythwukong/mods/1533)

| Prioridad | Candidato | Prueba que debe pasar |
| --- | --- | --- |
| Primera | Wolf Scout | Enemigo terrestre cuerpo a cuerpo. Confirmar que detecte al jugador y navegue. |
| Primera | Wolf Archer | Enemigo a distancia. Confirmar línea de tiro y que sus proyectiles se limpien. |
| Primera | Crow Diviner | Candidato volador. Confirmar que mantenga su comportamiento fuera del punto nativo. |
| Jefe piloto | Wandering Wight | Probar en un claro amplio con una sola instancia. Verificar que su muerte no entregue espíritu ni marque el encuentro original. |
| Jefes siguientes | Guangzhi y Tiger Vanguard | Probar sus ataques, cambios de estado, HUD y limpieza por separado. Habilitar solo en zonas autorizadas para cada uno. |
| Investigación | Whiteclad Noble, Yellow Wind Sage y Erlang | Evaluar dependencias de fases, terreno y guiones de arena. Publicarlos únicamente si funcionan como encuentros independientes. |

- Mostrar el `@usuario` sobre la criatura visible. Usar el nombre visible como respaldo y respetar el anonimato del live.
- Comenzar con un enemigo activo y 30 s de duración. Para el jefe piloto, usar un enfriamiento inicial de 180 s y bloquearlo con Modo sin morir. Confirmar que el enemigo entre en combate antes del anuncio.
- Al expirar, pausar, morir, viajar o desconectar, retirar solo actores y proyectiles de Cloud90. Confirmar que derrotarlos no afecte al diario, recompensas, progreso ni banderas de la historia.
- Considerar un efecto separado de revancha para jefes dependientes de su arena. Investigar el modo Desafío oficial y sus desbloqueos antes de conectarlo a regalos. [Anuncios de Game Science en Steam](https://steamcommunity.com/app/2358720/allnews/)

## Entrega

1. **Game Link** incorpora detección, portada, pestaña, pausa y un instalador del módulo. Verificar arranque, compatibilidad del cargador y retiro de archivos propios.
2. **Backend** incorpora catálogo, reglas con selección de enemigo y controles de seguridad. Modo troll apagado y Modo sin morir encendido de inicio. El overlay espera confirmación y el historial registra rechazos y resultados inciertos.
3. **Frontend** reutiliza la tarjeta y añade descarga e instrucciones una vez publicado el cliente compatible. El selector muestra los enemigos y jefes aprobados.
4. **Pruebas** cubren curación desde un regalo, HUD en topes, muerte, viaje, transformación, comandos repetidos y pérdida de conexión. Probar cada enemigo con partida secundaria y verificar limpieza y guardado. Registrar la combinación exacta de Steam, cargador y módulo.
