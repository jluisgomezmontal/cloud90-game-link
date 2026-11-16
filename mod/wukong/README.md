# Black Myth: Wukong en Game Link

Vista previa experimental para la versión de Steam en Windows. El plugin C# se compiló contra referencias públicas de Wukong. Falta identificar y probar el build instalado y la versión de B1CSharpLoader que carga el plugin. La vista previa usa C# porque las referencias públicas exponen métodos tipados de lectura, escritura y comprobación de atributos.

## Preparar una prueba

1. Instala [B1CSharpLoader](https://github.com/czastack/B1CSharpLoader) compatible con tu build. Abre la pestaña Black Myth: Wukong en Game Link y selecciona la carpeta de Steam que contiene `b1`. El cliente instala su propio DLL.
2. El catálogo está desactivado de inicio. En el backend de prueba, configura `CLOUD90_WUKONG_EXPERIMENTAL=1` y despliega el frontend correspondiente. Genera el token de Wukong y pégalo en el cliente.
3. Abre Wukong desde Game Link, carga una partida y prueba Curar al Destinado con vida incompleta. Comprueba el HUD, el historial y el anuncio. Luego prueba pausa, viaje, transformación y muerte. El mod suspende efectos durante transformaciones y estados de control restringido.

## Alcance

Hay 17 efectos implementados. Incluyen vida, maná, resistencia, concentración, Qi de espíritu y artefacto, escudo, ataque base, movimiento lento, bloqueo de esquiva, invulnerabilidad y muerte directa con permiso. Bendición y debilitamiento modifican el ataque base, por lo que se debe comprobar su efecto también en habilidades que escalen con ese atributo.

Sorbos, enfriamiento de un hechizo específico y energía de transformación requieren validar las API en el build de Steam.

## Emboscada experimental

El plugin admite varias invocaciones normales simultáneas y una emboscada de jefe, con 180 s de espera entre jefes. El catálogo se habilita por capacidad mientras el jugador esté en una zona de prueba. Cada orden espera que la criatura tenga al Destinado como objetivo antes de anunciarse.

La opción **Sorpresa** elige un enemigo normal del perfil de la zona activa en cada invocación.

Para probarla, coloca `ambush-profile.json` en `%LOCALAPPDATA%\Cloud90GameLink\wukong`. El perfil requiere el SHA-256 del ejecutable en uso y zonas verificadas. Las coordenadas usan unidades de Unreal. Cada ruta tiene que apuntar a una clase Blueprint de `BUTamerActor` en ese build.

```json
{
  "ExecutableSha256": "<SHA-256 del ejecutable>",
  "Zones": [
    {
      "Level": "<nombre interno del mapa>",
      "X": 0,
      "Y": 0,
      "Z": 0,
      "Radius": 1500,
      "Enemies": {
        "wolf_scout": "/Game/<ruta-verificada>.<clase>_C"
      }
    }
  ]
}
```

El ejemplo requiere los valores reales del build. Los IDs adicionales son `wolf_archer`, `crow_diviner`, `wandering_wight`, `guangzhi` y `tiger_vanguard`. Primero verifica Wolf Scout en una partida secundaria y confirma IA, etiquetas, limpieza de actores y proyectiles y ausencia de cambios en recompensas o progreso antes de habilitar más clases. Consulta el [plan de validación](../../PLAN-WUKONG.md#validación-y-publicación).

Para obtener datos del build, crea `capture-ambush.txt` vacío junto al perfil antes de abrir Wukong. Carga una partida cerca del enemigo candidato. En el primer estado controlable conectado, el módulo escribe `ambush-diagnostics.json` con el hash, mapa, coordenadas, clases de invocación cercanas y sus IDs cuando están cargadas. Este diagnóstico solo lee datos del juego. Quita el archivo de activación cuando termines.

## Compilar

Usa el directorio `CSharpMods` de las [referencias comunitarias](https://github.com/xyzkljl1/MyBlackMythWukongMods), que contiene `CSharpModBase.dll`, `0Harmony.dll` y `GameDll`. Con .NET SDK, empaqueta sobre Preview 4 para conservar Blasphemous y los módulos anteriores:

```powershell
.\build-gow-preview.ps1 -BaseExecutable 'C:\ruta\Preview4.exe' -WukongRefs 'C:\ruta\CSharpMods'
```
