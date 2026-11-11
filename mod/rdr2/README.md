# Diagnóstico de Red Dead Redemption 2

Primer paso de la integración de Game Link para la versión de Steam en Windows. El plugin usa las funciones nativas de Script Hook RDR2 para leer el contexto y la salud en modo Historia. La etiqueta `story_free_roam_candidate` señala que las comprobaciones disponibles coinciden con exploración libre. Su fiabilidad se debe comprobar en el juego antes de usarla para habilitar modificadores.

## Compilar

Extrae el SDK de [Script Hook RDR2](https://dev-c.com/rdr2/scripthookrdr2/) y ejecuta desde la raíz del repositorio con Visual Studio Build Tools y las herramientas C++ x64.

```powershell
.\mod\rdr2\build.ps1 -SdkPath 'C:\ruta\al\sdk'
```

El resultado es `mod\rdr2\dist\Cloud90.RDR2.Diagnostics.asi`.

## Probar en modo Historia

1. Termina la instalación en Steam, abre el juego y completa el primer inicio con Rockstar Games Launcher. En Steam, usa **Propiedades → Archivos instalados → Explorar** y anota la versión de `RDR2.exe`.
2. Con el juego cerrado, instala el runtime y el cargador ASI x64 compatibles siguiendo la guía de Script Hook. Si la carpeta ya tiene un cargador, identifica cuál es antes de sustituir archivos. Coloca `Cloud90.RDR2.Diagnostics.asi` junto a `RDR2.exe`.
3. Abre el juego desde Steam en modo Historia. Carga una partida, camina en exploración libre, pausa y vuelve al juego, monta un caballo, y entra a una misión o cinemática. Comprueba el registro en `%LOCALAPPDATA%\Cloud90GameLink\rdr2\diagnostics.log`. El registro se renueva al iniciar el plugin y recoge los cambios observados dos veces por segundo mientras el hilo de Script Hook está activo.
4. Compara las entradas del registro con el estado real del juego, sobre todo durante menús, cinemáticas y cargas. Antes de usar Red Dead Online, cierra el juego y retira los componentes de modding. Para retirar únicamente el diagnóstico, borra `Cloud90.RDR2.Diagnostics.asi`.

## Referencias

- [API y runtime de Script Hook RDR2](https://dev-c.com/rdr2/scripthookrdr2/)
- [Base de funciones nativas de RDR3](https://github.com/alloc8or/rdr3-nativedb-data)
