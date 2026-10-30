# MIGRACIÓN — TF2 Idle Manager v1.0.0

Origen recomendado: **v0.5.5.1**, Release Candidate validada en Windows. La v1.0.0 conserva los modelos de datos y el núcleo de automatización; su cambio principal es identidad de versión, documentación y preparación del paquete público.

1. Conserva tu carpeta v0.5.5.1 como respaldo.
2. Descomprime `TF2IdleManager_v1.0.0-src.zip` en una carpeta **nueva** si vas a compilar. Ejecuta `BUILD_RELEASE.bat` en Windows con .NET 8 SDK.
3. Comprueba que finalice la auditoría, extrae el ZIP portable generado a otra carpeta y realiza una sesión corta con Auto Class, ESC/minimización y STOP.
4. La configuración y los historiales siguen bajo `%LOCALAPPDATA%\TF2IdleManager\accounts\<SteamID64>`; **no los muevas ni los borres** para actualizar. Las claves siguen cifradas por DPAPI para tu usuario de Windows.
5. Si el nuevo ejecutable falla, cierra TF2 y la app, y vuelve al EXE anterior. No es necesario retroceder datos.

**No se incluye ninguna migración anterior a v1.0.0.** No compartas logs o CSV sin revisar SteamIDs y actividad.
