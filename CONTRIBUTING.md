# Contributing to TF2 Idle Manager

Thank you for considering a contribution. TF2 Idle Manager is a Windows/.NET 8 desktop project; issues, documentation updates, regression tests and carefully scoped bug fixes are welcome.

## Before opening a change

1. Check the [existing issues](https://github.com/SebaGutierrezF/tf2-idle-manager/issues).
2. For larger features or changes to TF2 automation, open an issue first to discuss scope and expected behavior.
3. **Never include** Steam API keys, account files, session histories, private logs, SteamIDs, personal paths or credentials in commits or issues.

## Local development

- Windows 10/11 x64 with the **.NET 8 SDK** and PowerShell.
- Clone the repository, open it in your editor, and run:

```bat
RUN_TESTS.bat
RUN_DEV.bat
```

- For a proposed release build:

```bat
BUILD_RELEASE.bat
```

The build must pass `AUDIT_RELEASE.ps1`. **Do not weaken the allowlist** to make a build pass. Releases are self-contained Windows x64 builds; there is no need to include local application data in a public artifact.

## Pull requests

- Prefer one focused change per PR; describe **why**, **what changed**, and **how it was tested**.
- Preserve working Auto Class, watchdog, full-server recovery and drop-notification behavior unless the issue specifically targets them.
- Include automated regression coverage for bug fixes where possible. For UI changes, include anonymized screenshots.
- Keep code and documentation compatible with existing per-SteamID64 data; do not overwrite existing user histories.
- Do not commit `bin/`, `obj/`, `release/`, `.env`, local settings, account folders, exported CSV files or logs.
- Use a descriptive PR title, such as `fix: prevent retry after STOP` or `docs: clarify portable installation`.

## Reporting a bug

Use the [Bug report template](https://github.com/SebaGutierrezF/tf2-idle-manager/issues/new/choose). Include version, Windows version, reproduction steps, expected vs. actual results and **redacted** log excerpts. In particular, remove keys and consider masking SteamID64 and server addresses.

For security vulnerabilities, **do not post exploit details publicly**; follow [SECURITY.md](SECURITY.md).

## Code of conduct

Please be respectful, constructive and focused on the project. This utility is unofficial; contributions must not impersonate Valve or present unofficial data as official.

---

### En español

Se aceptan reportes, documentación, pruebas y mejoras concretas. Antes de hacer un PR, ejecuta `RUN_TESTS.bat`; no compartas claves API, SteamID64, rutas personales ni registros sin censurar. Para vulnerabilidades, sigue `SECURITY.md`.
