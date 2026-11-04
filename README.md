# Core Banking API - Taller .NET 8

API local de cuentas bancarias implementada a partir del
[contrato original](docs/of-sys-acct-core-banking.yaml), con ASP.NET Core 8,
EF Core 8 y SQL Server 2022 Developer. Mambu y la emision de tokens se simulan.
El diseno no SOLID, los errores de negocio bajo HTTP 200 y la documentacion
Swagger parcial son decisiones deliberadas para el taller, no recomendaciones
para produccion. Ver [antipatrones y ejemplos de codigo](docs/antipatrones.md).

## Requisitos

- Docker Desktop iniciado con contenedores Linux x86-64.
- Memoria suficiente para SQL Server: al menos 2 GB para SQL; recomendado 4 GB para el conjunto.
- SDK compatible con net8.0 si se compila fuera de Docker.
- Node.js 20+ y npm para ejecutar Newman.

## Arranque

Desde la raiz, configurar `.env` usando [.env.example](.env.example).
Si `.env` ya existe, conservar sus valores.

```powershell
Copy-Item .env.example .env
docker compose up --build -d
docker compose ps -a
```

Los valores de ejemplo son credenciales ficticias PUBLICAS del taller. Nunca
usarlos con datos reales. `.env` no debe incluirse en Git ni en la imagen Docker.

| Servicio          | Direccion predeterminada                      |
| ----------------- | --------------------------------------------- |
| Swagger UI        | http://localhost:5000/swagger/index.html      |
| Documento OpenAPI | http://localhost:5000/swagger/v1/swagger.json |
| Salud de la base  | http://localhost:5000/health                  |
| SQL Server        | localhost,14333; base CoreBankingWorkshop     |

Si un puerto esta ocupado, cambiar API_PORT o SQL_PORT en `.env`. Actualizar
tambien origin en el entorno Postman si cambia API_PORT. No detener servicios
ajenos para liberar puertos.

[compose.yaml](compose.yaml) espera a SQL saludable, ejecuta el servicio migrate
una sola vez y levanta la API cuando termina con codigo 0. La API normal no aplica
migraciones al arrancar. [Dockerfile](Dockerfile) usa compilacion multietapa y
ejecuta la API con el usuario no root de la imagen .NET.

```powershell
docker compose logs --tail 30 migrate api
docker compose stop
docker compose start
```

El volumen sql-data conserva los datos. `docker compose down` no elimina ese
volumen; **down --volumes borra los datos** y no debe usarse salvo que se quiera
restablecer expresamente el taller.

## Compilacion y Migracion

```powershell
dotnet build src/CoreBanking.Api/CoreBanking.Api.csproj -c Release
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/CoreBanking.Api
dotnet run --project src/CoreBanking.Api -- --seed-info
```

La migracion inicial contiene esquema, datos y snapshot EF Core. No se usa
EnsureCreated ni se reinserta el seed en cada arranque. Para aplicar migraciones
pendientes sobre la misma base:

```powershell
docker compose run --rm migrate
```

Para ejecutar fuera de Docker hay que configurar ConnectionStrings__Banking,
Workshop__SigningKey, Workshop__ClientSecret y ASPNETCORE_ENVIRONMENT=Workshop.
dotnet no carga `.env` automaticamente. Ejemplo de arranque en otro puerto:

```powershell
dotnet run --project src/CoreBanking.Api -- --urls http://localhost:5002
```

## Datos Iniciales

En una base vacia, la migracion crea exactamente **5 productos, 1000 clientes
y 4000 cuentas**. Los POST posteriores pueden aumentar cuentas y movimientos.
Cada cliente tiene al menos una cuenta; los escenarios reservados comparten
cus-100045, por lo que no se exige exactamente cuatro cuentas por cliente.

Clientes: cus-100001 a cus-101000. Cuentas: acc-50001 a acc-54000.
Los cinco productos son EUR y tienen 800 cuentas cada uno:
CURRENT_ACCOUNT, REGULAR_SAVINGS, FIXED_DEPOSIT, SAVINGS_PLAN e INVESTOR_ACCOUNT.

| Estado             | Cantidad inicial |
| ------------------ | ---------------: |
| PENDING_APPROVAL   |              364 |
| APPROVED           |              364 |
| ACTIVE             |              364 |
| ACTIVE_IN_ARREARS  |              364 |
| MATURED            |              364 |
| LOCKED             |              364 |
| DORMANT            |              364 |
| CLOSED             |              363 |
| CLOSED_WRITTEN_OFF |              363 |
| WITHDRAWN          |              363 |
| CLOSED_REJECTED    |              363 |

| Cuenta    | Escenario inicial                                   |
| --------- | --------------------------------------------------- |
| acc-50001 | Disponible 5400.25, contable 5650.25, retencion 250 |
| acc-50002 | CLOSED con balances registrados cero                |
| acc-50003 | ACTIVE sin fondos; permite cierre                   |
| acc-50004 | ACTIVE con 10 EUR, sin sobregiro                    |
| acc-50005 | ACTIVE con -100 EUR, sobregiro configurado 500      |
| acc-50006 | Balance contable ausente                            |
| acc-50007 | Ambos balances ausentes                             |
| acc-50008 | CLOSED con disponible ausente                       |
| acc-50009 | ACTIVE con 1000 EUR; usada para ajustes             |
| acc-50010 | Disponible 50, contable 100, retencion 50           |
| acc-50011 | APPROVED sin fondos; permite cierre                 |
| acc-54000 | Cuenta de cus-101000; ajena a workshop-client       |

Los datos son sinteticos y deterministas. El corte inicial compartido es
2026-11-01T00:00:00Z; los ajustes actualizan el corte UTC. Las operaciones usan
decimal en C# y decimal(28,8) en SQL. En este taller EUR, los ajustes aceptan hasta
dos decimales; el exceso se rechaza, no se redondea. No se infiere valueDate.

## Operaciones y Scopes

Base: `/open-finance/system/accounts/v1`.

| Metodo | Ruta                                      | Scope           | Exito                           |
| ------ | ----------------------------------------- | --------------- | ------------------------------- |
| GET    | /customers/{customerId}/accounts          | accounts.read   | 200 lista                       |
| POST   | /customers/{customerId}/accounts          | accounts.write  | 201 PENDING_APPROVAL            |
| GET    | /accounts/{accountId}                     | accounts.read   | 200 referencia                  |
| GET    | /accounts/{accountId}/balances            | balances.read   | 200 balances o error de negocio |
| POST   | /accounts/{accountId}/closure             | accounts.close  | 200 CLOSED                      |
| POST   | /accounts/{accountId}/balance-adjustments | balances.adjust | 201 movimiento; 202 en proceso  |

Para solicitar un token local, usar POST `/oauth/token` con
application/x-www-form-urlencoded:

```text
grant_type=client_credentials
client_id=workshop-client
client_secret=workshop-only
scope=balances.read
```

Usar el secreto de WORKSHOP_CLIENT_SECRET si se cambio en `.env`. Cada scope
puede solicitarse por separado o combinarse con espacios. Scope omitido: todos;
scope vacio explicito: ninguno; scope desconocido: 400. El TTL predeterminado
es 60 minutos. scenario=expired emite un JWT firmado expirado para comprobar 401.

workshop-client accede al portfolio mock excepto cus-101000. workshop-other,
con el secreto del taller, solo accede a cus-101000. JWT valida firma HS256,
issuer, audience y expiracion; los scopes y la visibilidad de recursos se validan
antes de la logica de balances. Sin token: 401; scope insuficiente: 403;
recurso inexistente o ajeno: 404.

Swagger Authorize utiliza el endpoint local y permite seleccionar scopes.
Esta aplicacion solo arranca en Workshop/Development. Sin SigningKey configurada
genera una clave efimera: los tokens anteriores dejan de ser validos al reiniciar.

## Mambu Mock

[Program.cs](src/CoreBanking.Api/Program.cs) registra un HttpClient nombrado Mambu
con base https://mambu.invalid y
[MambuMockHandler](src/CoreBanking.Api/Integration/MambuMockHandler.cs).
El handler intercepta metodo, URI, body y headers y devuelve JSON simulado.
No se realizan llamadas de red a un tenant real. SQL es la fuente persistente;
el controlador pasa snapshots por HttpRequestMessage.Options y deserializa la
respuesta mock antes de guardar cambios.

Los campos y rutas del mock son una simulacion, no una integracion certificada
contra la OAS de un tenant. Ninguna clave mock debe enviarse a Mambu real.

## Errores de Balances: HTTP 200

Los codigos 677-680 son codigos de negocio inventados, no estados HTTP ni codigos
oficiales de Mambu. Devuelven **HTTP 200 OK**, application/json, sin balances parciales:

```json
{
  "errorCode": 677,
  "message": "Balance is not posible"
}
```

| Cuenta    | Codigo | Mensaje                                                      |
| --------- | -----: | ------------------------------------------------------------ |
| acc-50677 |    677 | Balance is not posible                                       |
| acc-50678 |    678 | Balance inquiry is not allowed for a locked account          |
| acc-50679 |    679 | Balance is unavailable while the account is pending approval |
| acc-50680 |    680 | Required balance information is missing                      |

acc-50006, acc-50007 y acc-50008 tambien devuelven 680 con HTTP 200. Una cuenta
CLOSED con balances completos permite consulta. Prioridad: bloqueo de consulta,
LOCKED, PENDING_APPROVAL y despues ausencia de balances. Se conserva expresamente
la ortografia del mensaje 677 solicitado. Los errores de autenticacion,
autorizacion, validacion o infraestructura no se convierten indiscriminadamente a 200.

## Ajustes y Cierre

Un ajuste positivo se traduce en deposito; uno negativo, en retiro. Reason queda
en auditoria SQL y no se envia a Mambu. Correcciones usan un nuevo movimiento
compensatorio, no transaction-adjust ni sobrescritura del movimiento original.

Solo ACTIVE permite ajustes. Moneda distinta, importe cero o escala invalida:
400. Estado incompatible o fondos mas sobregiro insuficientes: 409. Bloqueos
SQL y transaccion unen balances, movimiento y respuesta idempotente; sin posting parcial.

Idempotency-Key UUID v4 es obligatorio. La clave es unica por consumidor y
el payload incluye cuenta, importe normalizado, moneda y reason. Payload distinto:
409; duplicado Processing: 202; completado: replay del status/body original.
Fixture Processing: clave 11111111-1111-4111-8111-111111111111, cuenta acc-50009,
importe 1, EUR y reason `Pending workshop adjustment`.

ExpiresAt es metadata (24h predeterminadas); no hay limpieza automatica. Un
Processing abandonado requiere reconciliacion manual. Correlation-Id se preserva
o genera para cada solicitud; un replay de error conserva la correlacion original
en su body, aunque el header corresponda a la solicitud nueva.

Cierre mock: ACTIVE/APPROVED con balances cero y sin holds -> CLOSED.
CLOSED repetido devuelve 200; otras transiciones/precondiciones, 409.
Estas reglas son del taller, no reglas confirmadas de productos Mambu reales.

## Postman e Informes Newman

Importar [coleccion](postman/CoreBanking.postman_collection.json) y
[entorno local](postman/Local.postman_environment.json).
La coleccion tiene 409 solicitudes: tokens por scope, los seis endpoints,
validaciones, errores, idempotencia y 32 combinaciones de scopes frente a las
seis operaciones (192 casos). Las escrituras autorizadas de la matriz simulan
conflicto upstream para no alterar los datos antes de los happy paths.

Ejecutar siempre desde datos iniciales: los cierres y ajustes cambian cuentas.
El script prepara una base aislada nueva por ejecucion y restaura la API principal,
sin borrar bases existentes ni abrir puertos adicionales:

```powershell
.\postman\Run-Newman.ps1 -OpenReport
.\postman\Run-Newman.ps1 -Rebuild -OpenReport
```

La segunda variante reconstruye la imagen. Ver
[instrucciones de ejecucion Newman](postman/README.md).
Cada informe HTML/JSON se archiva en postman/reports con fecha e identificador.
Muestra entradas, scopes y respuestas con tokens y secretos ocultos. Las bases
aisladas se conservan y consumen espacio; no se eliminan automaticamente.

No se implementaron proyectos de tests ni assertions Postman. Los scripts
post-response solo capturan tokens/IDs; el ejecutor compara status y codigos de
negocio con los ejemplos esperados, no valida exhaustivamente cada schema/campo.
Ultima ejecucion verificada: 408 solicitudes, cero diferencias de estado/codigo
y cero errores de ejecucion. El caso manual de saturacion 429 esta excluido.

## Limites y Desviaciones

- El contrato original permanece sin modificar; la implementacion no pretende conformidad productiva completa.
- HTTP 200 para balances ausentes sustituye deliberadamente el 502 indicado en el contrato.
- Swagger conserva rutas y scopes, pero descripciones de entidades/operaciones y respuestas son parciales.
- SQL Compose usa sa y confianza del certificado local; exclusivamente para demostracion.
- No hay consentimiento, retencion auditada productiva, reconciliacion automatica ni configuracion real Mambu.
- El canal de transaccion, reglas de estados y escala EUR son decisiones locales del taller.

La documentacion oficial Microsoft Learn sirve para explicar riesgos y
alternativas en [antipatrones](docs/antipatrones.md). Su MCP no estaba disponible;
se aprobo usar las referencias oficiales por web en su lugar.
