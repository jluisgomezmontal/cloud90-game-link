# Black Myth: Wukong en Game Link

Vista previa experimental para la versión de Steam en Windows. El plugin C# se compiló contra referencias públicas de Wukong. Falta identificar y probar el build instalado y la versión de B1CSharpLoader que carga el plugin. La vista previa usa C# porque las referencias públicas exponen métodos tipados de lectura, escritura y comprobación de atributos.

## Preparar una prueba

1. Instala [B1CSharpLoader](https://github.com/czastack/B1CSharpLoader) compatible con tu build. Abre la pestaña Black Myth: Wukong en Game Link y selecciona la carpeta de Steam que contiene `b1`. El cliente instala su propio DLL.
2. El catálogo está desactivado de inicio. En el backend de prueba, configura `CLOUD90_WUKONG_EXPERIMENTAL=1` y despliega el frontend correspondiente. Genera el token de Wukong y pégalo en el cliente.
3. Abre Wukong desde Game Link, carga una partida y prueba Curar al Destinado con vida incompleta. Comprueba el HUD, el historial y el anuncio. Luego prueba pausa, viaje, transformación y muerte. El mod suspende efectos durante transformaciones y estados de control restringido.

## Alcance

Hay 17 efectos implementados. Incluyen vida, maná, resistencia, concentración, Qi de espíritu y artefacto, escudo, ataque base, movimiento lento, bloqueo de esquiva, invulnerabilidad y muerte directa con permiso. Bendición y debilitamiento modifican el ataque base, por lo que se debe comprobar su efecto también en habilidades que escalen con ese atributo.

Sorbos, enfriamiento de un hechizo específico, energía de transformación, emboscadas y jefes requieren validar las API y clases en el build de Steam. Para habilitar emboscadas se debe confirmar IA, limpieza de actores y proyectiles, y ausencia de cambios en recompensas o progreso. La etiqueta del remitente forma parte de esa fase.

## Compilar

Usa el directorio `CSharpMods` de las [referencias comunitarias](https://github.com/xyzkljl1/MyBlackMythWukongMods), que contiene `CSharpModBase.dll`, `0Harmony.dll` y `GameDll`. Con .NET SDK, empaqueta sobre Preview 4 para conservar Blasphemous y los módulos anteriores:

```powershell
.\build-gow-preview.ps1 -BaseExecutable 'C:\ruta\Preview4.exe' -WukongRefs 'C:\ruta\CSharpMods'
```
