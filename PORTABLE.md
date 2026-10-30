# TF2 Idle Manager v1.0.0 — Portable (Windows x64)

## Usuario final

1. Descarga `TF2IdleManager_v1.0.0-win-x64-portable.zip` desde la sección **Releases** del repositorio (una vez publicado).
2. Descomprime por completo en una carpeta nueva y ejecuta `TF2IdleManager.exe`. No se requiere instalar el runtime .NET por separado.
3. Ten Steam/Team Fortress 2 instalados y usa un servidor comunitario permitido. Para avisos de objetos proporciona tu propia clave Steam Web API y guárdala desde la interfaz.
4. `START IDLE` inicia y administra la sesión. El programa no acepta automáticamente drops; abre TF2 para reconocerlos. `STOP` desactiva la recuperación automática.

## Contenido permitido del ZIP

- `TF2IdleManager.exe`
- `README.md`, `PORTABLE.md`, `RELEASE-NOTES.txt`
- `assets/TF2IdleManager.ico`, `assets/TF2IdleManager.png`

Los datos, logs, historiales, capturas descargadas y claves API **NO** van dentro del ZIP. Se almacenan en `%LOCALAPPDATA%\TF2IdleManager`. Para copiar el programa a otro PC, mueve **solo el ZIP público**; no exportes claves, logs ni archivos de cuenta.

**Sin firma de código:** puede aparecer un aviso de SmartScreen. No recomendamos desactivar protecciones de Windows. Verifica la procedencia del repositorio y el SHA-256 publicado antes de abrir un ejecutable descargado.

## Mantenimiento

- Conserva un ZIP funcional anterior como respaldo. Para actualizar, extrae la nueva versión en **otra carpeta**.
- El historial existente se carga por usuario de Windows / SteamID64 sin reinstalarlo.
- Si necesitas soporte, comparte mensajes de error censurando SteamID, rutas y claves; no adjuntes tus archivos de `%LOCALAPPDATA%` sin revisarlos.
