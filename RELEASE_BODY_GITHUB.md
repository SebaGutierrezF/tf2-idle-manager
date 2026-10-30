# TF2 Idle Manager v1.0.0 — First public release 🎮

The first public Windows x64 portable release of **TF2 Idle Manager**, an unofficial Team Fortress 2 idle-session helper and drop notifier.

### Highlights

- Automatic class selection (all nine TF2 classes or Random), configured `+attack` / `+left`, and **ESC before minimizing**.
- Server-join watchdog: detects full/stalled servers, reconnects after map changes/disconnections and optionally tries saved backup servers.
- Item-drop notifications with names/images where available, corner placement and optional repeats until acknowledgment.
- Regular drops separated from cases/containers, with statistics and history **per SteamID64**.
- Dark/light dashboard, weekly comparison, session history and CSV export.
- Optional secondary Steam Web API key, both keys encrypted locally with Windows DPAPI.

### Installation

1. Download **`TF2IdleManager_v1.0.0-win-x64-portable.zip`** under **Assets** (not GitHub's automatic source-code archive).
2. Extract the ZIP completely and run `TF2IdleManager.exe` on Windows x64.
3. Configure Steam/TF2, a community server and an API key if you want drop notifications.

### Notes

- Items must be acknowledged manually inside TF2; no trading or auto-acceptance.
- Steam Web API availability is outside the app's control; errors such as HTTP 503 may delay alerts.
- Weekly remaining drops and next-drop timing are only estimates.
- Not affiliated with Valve/Steam. Unsigned binary; verify the SHA-256 checksum supplied with this release.

**Español:** descarga el **ZIP portable** de Assets, descomprímelo y ejecuta `TF2IdleManager.exe`. Los objetos deben reconocerse manualmente en TF2; el historial se separa por SteamID64 y las cajas no cuentan como drops regulares. Las estadísticas restantes son estimaciones.

---

**Release integrity:** attach the Windows-produced `SHA256SUMS-v1.0.0.txt` alongside the audited ZIP. Do not publish the source ZIP from this preparation step as though it were the compiled portable binary.
