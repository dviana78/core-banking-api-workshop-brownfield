# Ejecutar Newman y Generar el Informe HTML

Desde la raiz del proyecto, en PowerShell:

```powershell
.\postman\Run-Newman.ps1 -OpenReport
```

Para reconstruir tambien la imagen de la API despues de cambios de codigo:

```powershell
.\postman\Run-Newman.ps1 -Rebuild -OpenReport
```

Requisitos: Node.js 20+, npm, Docker Desktop con motor Linux activo y `.env`
configurado en la raiz. El script instala Newman con npm ci si falta.
Si la politica de PowerShell bloquea el script, ejecutar desde una terminal:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\postman\Run-Newman.ps1 -OpenReport
```

La excepcion de politica solo aplica a ese proceso, no cambia la configuracion
global del equipo. No requiere administrador.

## Comportamiento

- Regenera la coleccion y ejecuta los flujos con Newman, sin assertions.
- Usa una base nueva con el seed original en cada ejecucion; no borra datos existentes.
- Detiene temporalmente la API principal y usa su mismo API_PORT, sin abrir puertos adicionales.
- Restaura la API principal al terminar, incluso si Newman informa diferencias.
- Evita ejecuciones concurrentes del script mediante un mutex local.
- Conserva cada HTML y JSON en `postman/reports/<fecha-UTC-id>/`, sin sobrescribir informes anteriores.
- Oculta tokens y secretos en los informes.
- `-OpenReport` abre el HTML en el navegador predeterminado.
- Devuelve codigo 0 si no hay diferencias/errores, o 1 si los hay o falla el arranque.

El informe incluye entradas y respuestas de los seis endpoints, validaciones
400, caminos de error, 32 combinaciones de scopes y errores 677-680 con HTTP 200.
El escenario manual de saturacion 429 no forma parte de esta ejecucion.

La API no estara disponible para otras tareas durante el breve cambio entre
la base principal y la aislada. Las bases de ejecucion se conservan para inspeccion
y consumen espacio SQL; no se eliminan automaticamente.

Tambien puede ejecutarse directamente `node postman/run-newman.cjs`, pero ese
comando NO prepara una base limpia: tras ejecutar cierres o ajustes puede encontrar
diferencias por el estado de las cuentas. Para repetir todos los flujos, usar
Run-Newman.ps1.