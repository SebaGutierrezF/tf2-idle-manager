# Publicar TF2 Idle Manager v1.0.0 en GitHub

**Repositorio verificado:** https://github.com/SebaGutierrezF/tf2-idle-manager (publico, rama `main`, licencia MIT existente). **Aun no se ha publicado un Release.**

## 1. Preparar repositorio

1. El repositorio y su licencia MIT ya existen. **Conserva el archivo `LICENSE` en `main`**.
2. Sube el contenido de **la carpeta `TF2IdleManager_v1.0.0` a la raíz** del repositorio, no como una subcarpeta ni como un ZIP. Conserva `.github/workflows/windows-build.yml`, `.gitignore` y `docs/screenshots`.
3. El paquete especial `TF2IdleManager_v1.0.0-GitHub-Publicacion.zip` incluye `SUBIR_CODIGO_A_GITHUB.bat` para automatizar esta tarea con Git for Windows y confirmar el commit antes del push.
4. Confirma que no subiste `%LOCALAPPDATA%`, directorios `release/`, `bin/`, `obj/`, API keys, logs o CSV.

## 2. Auditoría y compilación definitiva

En Windows con .NET 8 SDK ejecuta `BUILD_RELEASE.bat`, como hiciste con la RC. Debe terminar con las 5 fases en OK y generar:

- `release/TF2IdleManager_v1.0.0-win-x64-portable.zip`
- `release/SHA256SUMS-v1.0.0.txt`

Prueba el EXE extraído en una carpeta distinta y marca `PLAN_DE_PRUEBAS.md`. El workflow GitHub Actions también ejecuta tests, build y auditoría en Windows; su artefacto es útil como comprobación independiente, pero no reemplaza tus pruebas con TF2 real.

## 3. Crear el Release

1. GitHub → repositorio → **Releases** → **Draft a new release**.
2. Tag: **`v1.0.0`**; target: la rama con la fuente revisada (habitualmente `main`).
3. Título: **`TF2 Idle Manager v1.0.0 — Windows Portable`**.
4. Copia el texto de `RELEASE_BODY_GITHUB.md` en la descripción.
5. Adjunta el **ZIP portable real generado en Windows** y `SHA256SUMS-v1.0.0.txt`. No adjuntes settings, logs, CSV ni keys.
6. Comprueba todo antes de pulsar **Publish release**. Si prefieres revisar primero, guarda como borrador.

**Latest:** marca esta primera versión estable como la más reciente al publicarla. No la etiquetes como pre-release si completaste las verificaciones.

## 4. Verificación final

Desde una sesión de navegador sin datos locales, abre la página del Release y verifica que el ZIP aparezca en **Assets**, se descargue correctamente y coincida con SHA-256. Confirma que README y capturas estén anonimizadas.

Para resolver bugs futuros: usa Issues/Discussions y solicita logs **redactados** (no claves ni SteamIDs en público).
