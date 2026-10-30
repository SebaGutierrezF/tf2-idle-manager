# Plan final de verificación — TF2 Idle Manager v1.0.0

**Una sola tanda antes de adjuntar el ZIP al release**. Estado heredado: la RC v0.5.5.1 ya pasó en el equipo del propietario los cinco pasos del build y las pruebas reales, incluido ESC/liberación del mouse. Eso no significa que se haya compilado todavía el binario marcado v1.0.0.

- [ ] **Build Windows**: ejecuta `BUILD_RELEASE.bat`. Debe pasar las pruebas offline, `dotnet publish`, auditoría de directorio y ZIP; se produce `SHA256SUMS-v1.0.0.txt`.
- [ ] **Paquete limpio**: ZIP con exactamente los 6 archivos permitidos. Sin `settings.json`, `accounts`, logs, CSV, `.dat` ni claves. No subir el directorio `%LOCALAPPDATA%`.
- [ ] **EXE portable**: extraer el ZIP en otra carpeta y ejecutar `TF2IdleManager.exe` sin `RUN_DEV.bat`; comprobar título `v1.0.0`, icono y pantalla About.
- [ ] **Conexión y clase**: iniciar en un servidor permitido; confirmar clase elegida, +attack/+left y ESC antes de minimizar. El ratón debe quedar libre.
- [ ] **Watchdog**: usar STOP para asegurar que no reconecte; si ocurre naturalmente cambio de mapa, comprobar que recupere la clase. Reintentos/failover previamente validados en la RC; repetir solo si el código funcional ha cambiado.
- [ ] **Drop monitor**: `Check now` y `Test image alert`; confirmar esquina/recordatorios y que no se inventen drops al hacer pruebas. Si ocurre un drop real, comprobar notificación y aceptación; no es necesario esperar a uno para aprobar el paquete.
- [ ] **Datos**: verificar historial por SteamID64 y CSV de ambas cuentas; los containers no deben aumentar el contador regular.
- [ ] **Segundo equipo / usuario limpio** (recomendado): verificar instalación sin archivos previos, sin keys ni historial inicial, y funcionamiento de la GUI. No subir datos personales como evidencia.
- [ ] **Publicación GitHub**: seleccionar licencia, repositorio, tag `v1.0.0`, adjuntar ZIP y checksum del build aprobado; usar `RELEASE_BODY_GITHUB.md`.

**Criterio:** la publicación pública requiere ZIP y checksum realmente generados y auditados en Windows. Si hay errores no se ignora `AUDIT_RELEASE.ps1`.
