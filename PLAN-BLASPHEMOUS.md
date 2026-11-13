# Blasphemous en Cloud90 Game Link

Alcance inicial para el primer Blasphemous de Steam en Windows, App ID 774361. La instalación local está en `D:\SteamLibrary\steamapps\common\Blasphemous` y su manifiesto registra el build `20206233`. Ese build es el punto de partida para las pruebas. La ficha de «Próximamente» ya usa el ID `blasphemous` y tiene portada y categorías. [Blasphemous en Steam](https://store.steampowered.com/app/774361/Blasphemous/)

## Modificadores para la primera entrega

Los porcentajes y enfriamientos son propuestas. Medir los valores efectivos con una partida secundaria, el HUD y las API del juego.

| Efecto | Comportamiento propuesto | Espera inicial |
| --- | --- | --- |
| Curar al Penitente | Recupera hasta el 25 % de su vida máxima actual. | 15 s |
| Vida al máximo | Rellena la barra hasta su máximo actual. | 45 s |
| Herida de penitencia | Quita hasta el 20 % de la vida máxima y conserva al menos 1 HP. | 30 s |
| Recuperar fervor | Recupera hasta el 30 % del máximo actual para usar plegarias. | 20 s |
| Fervor al máximo | Rellena la barra hasta su límite efectivo. | 45 s |
| Vaciar fervor | Deja el medidor en cero. Se rechaza durante el lanzamiento de una plegaria. | 45 s |
| Recargar un matraz biliar | Devuelve una carga consumida dentro del máximo desbloqueado. | 45 s |
| Quitar un matraz biliar | Retira una carga disponible. Se rechaza durante la animación de curación. | 60 s |
| Escudo del chat | Bloquea sabotajes de Cloud90 durante 15 s. | 30 s |

La lectura de vida y fervor tiene un precedente en DebugMod. La escritura y actualización del HUD deben verificarse en el build instalado. Para los matraces, confirmar cómo se guarda el número de cargas y si el juego trata los estados especiales de penitencia de forma distinta. [DebugMod](https://github.com/BrandenEK/Blasphemous.DebugMod)

## Segunda tanda

- **Matar al Penitente.** Usar la muerte normal del juego para respetar reaparición y las consecuencias de la partida. Requiere permiso explícito y desactivar Modo sin morir. Probar culpa y reaparición con una partida secundaria.
- **Bendición de combate y espada debilitada.** Subir o bajar temporalmente el daño de Mea Culpa, por ejemplo un 25 % durante 10 s. Aplicar el cambio al cálculo de daño del ataque y retirarlo al expirar.
- **Paso pesado y esquiva bloqueada.** Reducir movimiento o bloquear nuevas esquivas durante 3–4 s. Habilitar solo en suelo seguro y comprobar salas con plataformas, pinchos y puertas.
- **Invulnerabilidad breve.** Proteger contra golpes de combate durante 5 s. Verificar por separado los peligros del escenario y los ataques de jefes antes de anunciar su alcance.
- **Dar o quitar Lágrimas de Enmienda.** Usar cantidades acotadas y un permiso propio para cambios que pueden persistir. Verificar límites, HUD y persistencia después de guardar.

## Emboscada seleccionable

Probar primero tres perfiles de enemigos comunes del juego base, uno cuerpo a cuerpo, uno a distancia y uno volador. Seleccionar nombres y prefabs concretos tras inspeccionar el registro de enemigos y probarlos en salas compatibles. El Randomizer demuestra que hay un camino para cargar y sustituir enemigos en el primer Blasphemous. Invocarlos durante una partida requiere validar su inicialización, IA y limpieza por separado. [Código de Enemy Randomizer](https://github.com/BrandenEK/Blasphemous.Randomizer/tree/main/Blasphemous.Randomizer/EnemyRando)

- Permitir un enemigo invocado a la vez y elegirlo por regalo. Buscar suelo libre para enemigos terrestres y espacio despejado para los voladores. Excluir jefes, arenas con guion, transiciones y habitaciones sin un punto válido.
- Añadir sobre la entidad el `@usuario` del remitente cuando la plataforma lo entregue. Usar el nombre visible como respaldo y respetar las reglas de anonimato de Cloud90.
- Confirmar que el enemigo se activó antes de anunciarlo. Retirar únicamente las entidades de Cloud90 al morir, salir de la sala, pausar, desconectar o expirar el tiempo. Verificar que sus muertes no activen puertas, objetivos ni recompensas del mapa.

## Orden de integración

1. **Mod de diagnóstico.** Comparar la Modding API de Blasphemous con el patrón BepInEx de Cuphead. Fijar la combinación compatible con el build de Steam, cargar un módulo C# en una copia de prueba y registrar sala, vida, fervor y matraces. Confirmar el estado controlable en menús, muerte, jefes y transiciones. [Modding API](https://github.com/BrandenEK/Blasphemous.ModdingAPI) · [Instalador de mods](https://github.com/BrandenEK/Blasphemous.Modding.Installer)
2. **Curación de extremo a extremo.** Crear la pestaña de Game Link con detección por `appmanifest_774361.acf`, ruta manual, token propio y una instalación que preserve otros mods. Reutilizar el buzón local con latido, contexto de sala, caducidad y confirmaciones. Probar regalo → Cloud90 Live → Game Link → curación → historial y overlay. El overlay anuncia cuando el mod confirma la aplicación.
3. **Completar los efectos iniciales.** Aplicar efectos en el hilo del juego, limitar valores y rechazar órdenes duplicadas o antiguas. Comprobar topes, efecto de la pausa, pérdida de conexión y limpieza al cambiar de sala. Dejar Modo troll apagado y Modo sin morir encendido por defecto.
4. **Publicación coordinada.** Abrir PR de Game Link, backend y frontend. Publicar el cliente compatible, añadir su botón de descarga e instrucciones, y activar el catálogo cuando esté disponible la compilación. Verificar que la ficha se mueva de «Próximamente» al catálogo sin duplicarse.
5. **Ampliar después de las pruebas.** Añadir sabotajes temporales, muerte y emboscadas cuando la primera tanda funcione en Steam. Probar con enemigos activos, junto a un Prie Dieu, con los medidores llenos y vacíos, y durante muerte, pausa y cambio de sala.
