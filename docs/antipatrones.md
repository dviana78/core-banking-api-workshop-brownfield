# Antipatrones Aplicados y Ejemplos

Estos antipatrones se introdujeron deliberadamente para el taller solicitado.
No son recomendaciones de Microsoft ni una arquitectura para produccion.
Los ejemplos siguientes son extractos reales del codigo actual; no son codigo
alternativo inventado. Se explican tambien alternativas, sin aplicarlas.

## 1. Controlador Con Multiples Responsabilidades

Ubicacion: [AccountsController, CreateAccount y CreateBalanceAdjustment](../src/CoreBanking.Api/Controllers/AccountsController.cs).

```csharp
var product = await database.Products.SingleOrDefaultAsync(item => item.AccountType == request.AccountType && item.Currency == request.Currency, cancellationToken);
```

```csharp
var source = await Send(HttpMethod.Post, "/api/deposits", new
{
    name = request.Name, accountHolderKey = customer.HolderKey, accountHolderType = "CLIENT", productTypeKey = product.Id
}, Source(account), cancellationToken);
```

```csharp
database.Accounts.Add(account);
await database.SaveChangesAsync(cancellationToken);
```

El mismo controlador decide autorizacion de recursos, valida productos,
construye requests Mambu, transforma JSON, controla SQL, ejecuta reglas financieras
y guarda auditoria/idempotencia. Tiene varios motivos independientes de cambio:
viola responsabilidad unica (SRP) y separacion de responsabilidades.

Riesgo: modificar el proveedor o una regla obliga a tocar el mismo archivo y
puede afectar otras operaciones. Alternativa: separar los casos de uso y reglas
del transporte y la persistencia cuando el objetivo deje de ser didactico.

Fuente: [Microsoft, separation of concerns y single responsibility](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/architectural-principles).

## 2. Negocio Acoplado a Persistencia y Proveedor

Ubicacion: [AccountsController, constructor y LockedAccount](../src/CoreBanking.Api/Controllers/AccountsController.cs).

```csharp
public class AccountsController(BankingDbContext database, IHttpClientFactory clients, IConfiguration configuration) : ControllerBase
```

```csharp
private Task<Account> LockedAccount(string accountId, CancellationToken cancellationToken) => database.Accounts
    .FromSqlInterpolated($"SELECT * FROM [Accounts] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {accountId}").SingleAsync(cancellationToken);
```

La logica de negocio depende de BankingDbContext concreto, nombres SQL y campos
Mambu. Cambiar la tecnologia de persistencia o el proveedor exige cambiar el
controlador. La inyeccion de dependencias del framework no elimina ese acoplamiento
ni equivale por si sola a inversion de dependencias (DIP).

Los parametros SQL y los bloqueos son controles necesarios, no antipatrones.
El problema es donde se mezclan las decisiones, no la ausencia de repositorios
o interfaces por si sola. Alternativa: encapsular capacidades de persistencia
y proveedor detras de dependencias apropiadas al negocio.

Fuente: [Microsoft, dependency inversion](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/architectural-principles#dependency-inversion).

## 3. Reglas Rigidas y Literales Dispersos

Ubicacion: [AccountsController, CloseAccount y CreateBalanceAdjustment](../src/CoreBanking.Api/Controllers/AccountsController.cs).

```csharp
if (account.AccountState is not ("ACTIVE" or "APPROVED") || account.BookBalance != 0 || account.AvailableBalance != 0 || account.Holds != 0)
    return Failure(409, "Account closure preconditions are not met");
```

```csharp
if (account.AccountState != "ACTIVE") throw new WorkshopFailure(409, "Account state does not allow adjustments");
```

```csharp
new { amount = Math.Abs(amount), currencyCode = request.Currency, transactionDetails = new { transactionChannelKey = "mock-channel-workshop" } },
```

El ultimo extracto es un argumento de Send. ACTIVE, CLOSED, PENDING_APPROVAL y
EUR aparecen en distintas ramas; el canal es un literal interno. Cambiar reglas
o catalogos obliga a editar condicionales y otros archivos, incluido el
[generador Postman](../postman/generate.cjs). Se facilita duplicacion incoherente
(DRY) y se dificulta extender reglas sin modificar decisiones existentes (OCP).

No se afirma que toda constante sea mala o que un switch siempre viole OCP.
Aqui la rigidez procede de representar politica cambiante del tenant mediante
literales de taller repartidos. Alternativa: catalogos y reglas configurables,
centralizando solo conceptos que realmente comparten significado.

Fuente: [Microsoft, DRY y principios arquitectonicos](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/architectural-principles#dont-repeat-yourself-dry).

## 4. Invariantes Fuera de la Entidad

Ubicacion: [AccountsController, CreateBalanceAdjustment](../src/CoreBanking.Api/Controllers/AccountsController.cs);
modelo: [Entities.cs, Account](../src/CoreBanking.Api/Data/Entities.cs).

```csharp
account.BookBalance += amount;
account.AvailableBalance += amount;
account.AsOf = adjustment.RecordedAt;
database.Adjustments.Add(adjustment);
```

Account expone propiedades mutables; los invariantes se verifican fuera de ella,
en el controlador. Otro futuro consumidor puede cambiar el estado directamente
sin reutilizar las mismas reglas. Esto limita encapsulacion del modelo y produce
un modelo anemico en un dominio con invariantes financieros.

No es incorrecto usar DTOs o POCOs simples por si mismos. El riesgo concreto
es tener reglas financieras fuera del unico lugar que puede mutar balances.
Actualmente las operaciones implementadas siguen usando decimal, transacciones
y controles de fondos. Alternativa: transiciones del dominio encapsuladas y
reglas reutilizables, sin eliminar la atomicidad SQL.

Fuente: [Microsoft, encapsulation](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/architectural-principles#encapsulation).

## 5. Fallos de Negocio Representados Como Exito HTTP

Ubicacion: [AccountsController, GetAccountBalances](../src/CoreBanking.Api/Controllers/AccountsController.cs).

```csharp
if (account.BalanceInquiryBlocked) return Ok(new BalanceBusinessError(677, "Balance is not posible"));
if (reference.AccountState == "LOCKED") return Ok(new BalanceBusinessError(678, "Balance inquiry is not allowed for a locked account"));
if (reference.AccountState == "PENDING_APPROVAL") return Ok(new BalanceBusinessError(679, "Balance is unavailable while the account is pending approval"));
```

```csharp
return Ok(new BalanceBusinessError(680, "Required balance information is missing"));
```

Todos devuelven HTTP 200, incluso ausencia de balances, que el contrato original
mapea a 502. Un consumidor que solo mire HTTP, metricas de exito o reglas de
reintento no distingue el fallo. La respuesta cambia de AccountBalance a
BalanceBusinessError, mientras otros fallos usan Problem Details.

Es una desviacion expresamente solicitada y se mantiene. Los codigos no son
oficiales Mambu. La ortografia de 677 tambien se conserva deliberadamente.
Alternativa productiva: status HTTP apropiado y codigo de negocio como extension
de Problem Details; ese cambio NO se ha aplicado.

Fuente: [Microsoft, Problem Details](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-8.0#problem-details).

## 6. OpenAPI Incompleto Frente al Comportamiento Real

Ubicacion: [AccountsController, CloseAccount](../src/CoreBanking.Api/Controllers/AccountsController.cs)
y [Contracts.cs, modelos publicados](../src/CoreBanking.Api/Models/Contracts.cs).

```csharp
[SwaggerOperation(Summary = "Close account")]
[ProducesResponseType(typeof(AccountReference), 200)]
```

CloseAccount puede devolver 400/401/403/404/409 y fallos de infraestructura, pero
los atributos solo documentan su exito y una descripcion breve. GetAccount y
CreateBalanceAdjustment tampoco tienen descripcion extendida. En los modelos,
algunas propiedades tienen SwaggerSchema y otras no; por ejemplo AccountReference
describe accountId/accountType, pero no currency/accountState.

Ademas, balances documenta AccountBalance para 200 sin enumerar completamente
los cuatro errores de negocio con el mismo status. Rutas y scopes no se ocultan,
y las validaciones runtime se mantienen. El riesgo es que usuarios/generadores
interpreten una documentacion parcial como contrato completo.

Alternativa: documentar variantes, ejemplos, errores y propiedades antes de
publicar en produccion. La documentacion parcial solicitada no se ha completado
en Swagger; el README/Postman explican los escenarios.

Fuente: [Microsoft, Swagger/OpenAPI](https://learn.microsoft.com/en-us/aspnet/core/tutorials/web-api-help-pages-using-swagger?view=aspnetcore-8.0).

## 7. Replay de Errores Con Correlacion Antigua

Ubicacion: [AccountsController, Replay](../src/CoreBanking.Api/Controllers/AccountsController.cs).

```csharp
return new ContentResult { StatusCode = record.Status, ContentType = record.Status >= 400 ? "application/problem+json" : "application/json", Content = record.Response };
```

ProblemBody incluye la correlacion original al guardar el error. Replay conserva
el body, pero el middleware genera/preserva el header de la solicitud nueva.
Si el caller cambia Correlation-Id, el body y el header pueden diferir. Es una
limitacion real de la implementacion, no una propiedad deseable de idempotencia.

Riesgo: trazabilidad ambigua. Alternativa: definir que partes del replay son
inmutables y como expresar correlacion de operacion original y solicitud actual,
antes de afirmar conformidad con el contrato de correlacion.

Fuente relacionada: [Microsoft, personalizacion de Problem Details](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-8.0#customize-problem-details).

## Controles Que No Se Consideran Antipatrones

- IHttpClientFactory y el handler mock evitan crear conexiones por peticion y llamadas reales con claves sinteticas.
- SQL parametrizado, FK, clave idempotente unica, rowversion y transacciones protegen persistencia y concurrencia.
- Los balances usan decimal, nunca float/double, y no se aceptan postings parciales.
- JWT/scopes/visibilidad se validan; 401/403/404 ocurren antes de los errores de balances.
- Compose ejecuta migracion one-shot y conserva el volumen, no migra desde cada replica API.

Referencias oficiales adicionales:
[HttpClient e IHttpClientFactory](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-8.0),
[aplicacion de migraciones EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying),
[SQL Server en Docker](https://learn.microsoft.com/en-us/sql/linux/quickstart-install-connect-docker?view=sql-server-ver16).

Otros limites de taller: credenciales publicas de ejemplo, uso SQL de sa y
TrustServerCertificate, retencion sin limpieza automatica y Processing sin
reconciliacion. No se presentan como controles productivos suficientes.

El MCP de documentacion Microsoft no estaba disponible durante el trabajo;
se aprobo consultar las fuentes oficiales por web. Las fuentes describen buenas
practicas que se contrastan con el codigo: NO aconsejan introducir antipatrones.