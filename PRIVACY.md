# Data and privacy / Datos y privacidad

TF2 Idle Manager is a local desktop application; it is not a cloud account service.

**Stored locally:**

- `%LOCALAPPDATA%\TF2IdleManager\settings.json` — application preferences and saved server choices.
- `%LOCALAPPDATA%\TF2IdleManager\accounts\<SteamID64>` — drop history, weekly tracking and managed session records per SteamID.
- Log files and image/metadata cache in the application data directory.
- Primary and optional secondary Steam Web API key in separate DPAPI-protected `.dat` files. DPAPI encryption is bound to the current Windows user; copying encrypted files to another user/PC typically does not transfer usable keys.

**Network communications:** Steam Web API requests for TF2 inventory/schema/images, and TF2 itself connecting to Steam or configured community servers. The application has no independent upload service in this release. An API key is a secret: do not paste it into bug reports or GitHub issues.

**Data sharing:** public release ZIP is audited to exclude these local files. User-exported CSVs and manually attached logs can include SteamIDs, timestamps, server addresses and item history. Treat those as personal records.

**No automatic acknowledgment or commerce:** inventory alerts are informative only; item recognition is performed by the user inside TF2. The application does not buy, sell or trade items.

**No telemetry claim:** the app does not implement a custom telemetry backend. This does not describe communications made by Steam, Valve or individual game servers.

En español: las cuentas se separan por SteamID64, las claves se cifran con DPAPI y los archivos personales permanecen en `%LOCALAPPDATA%`. El ZIP público no incluye esos datos; revisa CSV y logs antes de compartirlos.
