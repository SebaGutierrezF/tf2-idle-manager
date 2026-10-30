# Support / Soporte

Thanks for using **TF2 Idle Manager**. This is an unofficial community project.

## Where to get help

- **Bug:** [open a bug report](https://github.com/SebaGutierrezF/tf2-idle-manager/issues/new/choose).
- **Feature idea:** use the same [Issues form](https://github.com/SebaGutierrezF/tf2-idle-manager/issues/new/choose).
- **Security concern:** read [SECURITY.md](SECURITY.md); avoid public disclosure.
- **Current download:** [latest release](https://github.com/SebaGutierrezF/tf2-idle-manager/releases/latest).

## Before reporting

- Confirm you are using the latest portable release and extracted the entire ZIP.
- Note your app version, Windows version, the last visible step and what you expected.
- Include short, **redacted** log excerpts only. Never post Steam Web API keys, private account files, passwords, raw settings, or personal SteamIDs.

## Common questions

**TF2 opens but does not start idling.** The server may be full, disconnected or waiting for a menu. Check the watchdog status, server presets and Auto Class messages.

**My drop alert did not appear.** Check monitoring preferences, whether TF2 has an unacknowledged item and whether the Steam Web API is responding. HTTP 503 errors can delay detection.

**Can the app accept a drop automatically?** No. Acknowledge drops manually inside TF2; the app tracks their status.

**Are weekly drops exact?** No. Remaining-drop counts and next-drop timing are estimates. Cases are tracked separately.

**Does the portable app contain my history or API keys?** No, the release package is audited for a small allowlist. Your data lives in `%LOCALAPPDATA%\TF2IdleManager` on your computer.

---

### En español

Para errores y sugerencias usa [Issues](https://github.com/SebaGutierrezF/tf2-idle-manager/issues/new/choose). Indica la versión y los pasos para reproducir el problema, pero **censura SteamID64, claves y rutas privadas**. Si la API devuelve 503 puede retrasar los avisos; los drops se aceptan manualmente en TF2.
