# Auditoría de publicación — TF2 Idle Manager v1.0.0

## Procedencia

- Base verificada por el propietario en Windows: v0.5.5.1 RC.
- Cambios v1.0.0: identidad visible y numeración, scripts/documentación pública, screenshots anonimizadas, `.gitignore` y workflow de integración Windows.
- El núcleo funcional (Auto Class, drop monitoring, watchdog, contabilidad, toasts) se conserva. Solo cambiaron cadenas de branding/comentarios donde correspondía.

## Controles de distribución

`BUILD_RELEASE.bat` ejecuta pruebas offline y obliga a `AUDIT_RELEASE.ps1` antes y después de empaquetar. La lista permitida contiene exactamente seis archivos; un séptimo provoca error. El checksum se genera fuera del ZIP una vez que pasa la auditoría.

No hay credenciales Steam API ni archivos de cuenta en el código fuente distribuido. **Eso no sustituye la inspección final de cualquier captura, log o exportación que se adjunte manualmente.**

## Riesgos que deben comunicarse

- Depende de TF2, Steam, los servidores comunitarios y el acceso a Steam Web API.
- La disponibilidad de drops puede perderse durante HTTP 503; las alertas pueden demorarse.
- Reintentos, failover y simulación de teclado pueden depender del entorno gráfico/permisos.
- No firmado digitalmente; Windows SmartScreen puede alertar.
- Las métricas semanales son aproximaciones y no representan los límites internos exactos de Valve.
- Los datos históricos antiguos no permiten reconstruir duraciones de sesión no almacenadas.

## Estado de la versión

- Validación estática y ZIP de **código fuente** realizada en el entorno de elaboración.
- Pruebas funcionales de la RC v0.5.5.1 en Windows: confirmadas por el propietario.
- Compilación y ejecución de **v1.0.0** en Windows: pendientes de la tanda final; no declarar la nueva etiqueta como compilada hasta completarlas.
