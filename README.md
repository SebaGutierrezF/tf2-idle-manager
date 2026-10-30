# TF2 Idle Manager v1.0.0

**Unofficial desktop utility for Team Fortress 2 idle sessions and item-drop notifications.**  
Windows 10/11 (x64) · .NET 8 WinForms · portable, self-contained build.

> **Status:** v1.0.0 source prepared from the user-validated v0.5.5.1 release candidate. Build and audit the public portable ZIP on Windows before attaching it to a GitHub Release. This source ZIP **does not contain a prebuilt EXE**.

## Features

- Connect to a chosen TF2 community idle server, select one of nine classes or Random, and issue `+attack` / `+left` as configured.
- Automatically handle initial welcome/team/class screens; send **ESC before minimizing** to help release the mouse cursor.
- Detect full or stalled servers, recover from disconnections, process restarts and map changes; optional failover to saved servers.
- Notify about TF2 unacknowledged item drops using Steam Web API; item name and image when metadata is available.
- Optional repeated notifications, configurable position on any of four screen corners and dark/light themes.
- Separate regular drops from containers/cases. History, weekly estimates and session logs are isolated by **SteamID64**.
- Primary and optional secondary Steam Web API keys encrypted for the current Windows user with **DPAPI**; no keys bundled with the portable EXE.
- CSV export, session and weekly comparisons, and a read-only item history. **Items are not acknowledged automatically.**

## Download and install (portable users)

1. Open the repository's **Releases** page and download **`TF2IdleManager_v1.0.0-win-x64-portable.zip`** (once published). **Do not use GitHub's auto-generated “Source code” ZIP as the application executable.**
2. Extract the entire portable ZIP to a normal writable folder, away from older versions. Do not run the EXE from within the ZIP.
3. Open `TF2IdleManager.exe`. Steam and TF2 must be installed locally. Configure a TF2 idle server and, if you want drop alerts, a Steam Web API key.
4. Choose a class and press **START IDLE**. Drops are acknowledged manually inside TF2; **STOP** ends the managed session.

The portable package contains an EXE, two icon assets and documentation. It does not require installing the .NET 8 runtime separately. More: `PORTABLE.md`.

## Build from source (Windows)

Requires the **.NET 8 SDK** and PowerShell:

```bat
BUILD_RELEASE.bat
```

The builder executes offline regression tests, publishes a self-contained `win-x64` EXE, audits an exact allowlist of distributed files, packages the ZIP, repeats the audit and writes a SHA-256 checksum. **Do not bypass `AUDIT_RELEASE.ps1`.**

For development:

```bat
RUN_DEV.bat
```

The sources include a Windows GitHub Actions workflow that builds and audits the ZIP. See `PUBLISH_GITHUB.md` for release instructions.

## Privacy and data

Data is local at `%LOCALAPPDATA%\TF2IdleManager`; histories and weekly/session data use `accounts\<SteamID64>`. API keys are protected with Windows DPAPI and are not portable between Windows users. CSV exports and logs **may contain SteamIDs and activity information**, so review them before sharing. See `PRIVACY.md`.

## Known limitations

- The TF2/Steam Web API can respond with `503` or other temporary errors; alerts may be delayed or unavailable until it recovers. A backup key cannot fix a Valve-wide outage.
- Time-to-next-drop and number of remaining regular weekly drops are **estimates**, never exact Valve counters. Cases/containers are recorded separately.
- Automatic entry depends on Steam, the TF2 client, third-party servers, Windows window focus and user permissions. Full servers may delay an idle session.
- No auto-acknowledgment, auto-trading, buying or selling. This is **not** a Valve/Steam-approved or affiliated product. Follow the game and server rules.
- No code signing certificate is included; Windows may present a SmartScreen warning. Do not disable Windows security protections to run untrusted files.

## Screenshots and release notes

Sanitized screenshots of **previous validated 0.5.x UI builds** are in [`docs/SCREENSHOTS.md`](docs/SCREENSHOTS.md); they are illustrative, not a claim that a v1.0.0 binary was captured. Release description: `RELEASE_BODY_GITHUB.md`.

## License

**MIT License.** Source code is licensed under the MIT License already published in the repository (`LICENSE`). Team Fortress 2, Steam, game artwork and related trademarks belong to their respective owners; this utility is unofficial and not affiliated with Valve.

---

## Resumen en español

**TF2 Idle Manager** permite iniciar sesiones en servidores idle comunitarios de TF2, elegir clase, ejecutar los comandos configurados, recuperar conexiones y recibir avisos de objetos aún pendientes de reconocer. Incluye estadísticas separadas por cuenta, contenedores aparte de drops regulares, historial visual y exportación CSV.

Para usarlo, descarga el **ZIP portable de GitHub Releases**, descomprímelo y ejecuta `TF2IdleManager.exe`; el ZIP de código fuente **no es el ejecutable**. Las alertas necesitan una Steam Web API key válida; si Valve devuelve `503`, la notificación puede retrasarse. El programa **no acepta los objetos por ti**. Las horas y drops restantes son estimaciones, no cifras oficiales.

El programa guarda sus datos en `%LOCALAPPDATA%\TF2IdleManager` y cifra las claves con DPAPI. Es una herramienta no oficial y no está asociada a Valve.
