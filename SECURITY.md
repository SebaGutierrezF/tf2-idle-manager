# Security Policy / Política de seguridad

## Supported versions

| Version | Security updates |
|:--|:--|
| `1.0.x` | Supported |
| `< 1.0` pre-release builds | Not supported for public use |

This policy describes **current release maintenance intentions**, not a warranty or service-level commitment.

## Reporting a vulnerability

**Please do not open a public issue with exploit details, API keys, tokens, personal SteamIDs or private logs.**

1. Use GitHub **Private vulnerability reporting** / **Report a vulnerability** in the repository's Security tab **if available**.
2. If private reporting is not enabled, open a brief public issue requesting a private reporting channel **without disclosing exploit steps or secrets**.
3. Include the affected app version, Windows version, impact and safe reproduction steps once a private channel is agreed.

We will review reports as feasible; no fixed response time or bounty is promised.

## Data protection notes

- API keys are stored using Windows **DPAPI** for the local Windows user; this is not a cloud-secret manager.
- Logs and CSV exports can include SteamIDs, timestamps and server addresses; redact them before sharing.
- Official release ZIPs are built with an audited file allowlist and a SHA-256 checksum; this does not replace code signing or external security review.
- The application is **not affiliated with Valve** and does not provide an auto-accept, trading or sales workflow.

See [PRIVACY.md](PRIVACY.md) and [SUPPORT.md](SUPPORT.md).

---

### En español

Si encuentras una vulnerabilidad, **no publiques detalles de explotación ni credenciales** en Issues. Utiliza la opción privada de GitHub Security si está disponible; si no, solicita un canal privado sin exponer información sensible.
