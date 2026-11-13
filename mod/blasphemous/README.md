# Blasphemous 1 en Game Link

Vista previa para Steam build 20206233 en Windows. Instala [BepInEx 5.4.23.4 x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.4) junto a Blasphemous.exe. Abre la pestaña Blasphemous en Game Link, pega el token de Cloud90 Live y abre el juego desde el cliente. Game Link instala su propio plugin en BepInEx/plugins y permite retirarlo desde la pestaña.

Prueba primero **Curar al Penitente** con vida incompleta. Comprueba el cambio en el HUD y la confirmación en Cloud90 Live. Después prueba vida, fervor y matraces en sus límites. La penitencia con unidades de vida tiene restricciones para los efectos de vida. F9 alterna la pausa del mod.

Para probar la muerte directa, habilita Modo troll, desactiva Modo sin morir y habilita el permiso de muerte y cambios de Lágrimas. Los cambios de Lágrimas pueden persistir al guardar. La invulnerabilidad breve protege contra golpes de enemigos compatibles. Los peligros del escenario conservan su comportamiento normal.

Emboscada permite elegir Flagelante, Lanzapiedras o Fantasma de la campana. Usa puntos nativos cercanos de esa familia que estén vacantes en la sala. El registro puede indicar que no hay un punto compatible. La etiqueta usa el remitente del regalo. Comprueba que el enemigo ataque, desaparezca al salir o pausar, y que no avance objetivos ni entregue Lágrimas. Usa una partida secundaria para estas pruebas.

## Compilar

Usa el directorio Managed del juego, BepInEx/core de la distribución oficial y el ejecutable de Game Link Preview 3 como base para preservar sus módulos.

```powershell
.\build-gow-preview.ps1 -BaseExecutable 'C:\ruta\Preview3.exe' -BlasphemousManaged 'C:\ruta\Blasphemous_Data\Managed' -BlasphemousLoaderCore 'C:\ruta\BepInEx\core'
```

Referencias de investigación. [API comunitaria](https://github.com/BrandenEK/Blasphemous.ModdingAPI) · [Registro de enemigos del Randomizer](https://github.com/BrandenEK/Blasphemous.Randomizer/tree/main/Blasphemous.Randomizer/EnemyRando)
