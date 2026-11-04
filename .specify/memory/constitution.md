# Core Banking API Brownfield Constitution

## Purpose and Scope

This constitution governs the incremental modernization of the existing Core Banking API workshop repository. It distinguishes observed legacy behavior from the approved target architecture. The current implementation is a local workshop simulation, not a production banking service or a certified integration with a real Mambu tenant.

The constitution is binding for implementation, tests, documentation, and review. An approved feature specification may make scoped decisions within these principles; it cannot silently waive or weaken them. A change to a constitutional invariant requires an explicit, reviewed amendment and a compatibility decision.

## Current Baseline and Target

At ratification, `src/CoreBanking.Api/CoreBanking.Api.csproj` defines one ASP.NET Core Web SDK project targeting `net8.0`, with nullable reference types and implicit usings enabled. Its direct dependencies include EF Core SQL Server/Design and JWT bearer authentication at 8.0.28, plus Swashbuckle at 6.9.0. The API is currently organized under `Controllers`, `Data`, `Integration`, and `Models`; `Program.cs` composes the web host, database, mock, authentication, and middleware. This is the legacy baseline, not the desired final architecture.

The workshop target is an incremental move to C# on `net10.0`, Clean Architecture, centrally managed NuGet versions, FastEndpoints and FastEndpoints.Swagger 8.3.0, automated unit/functional/integration tests, and local Aspire orchestration. The migration must be specified, tested, and reviewed in increments. Do not claim that a target component exists until it has actually been implemented and verified. Preserve `CoreBanking.Api` as the API entry project unless an approved specification justifies otherwise.

## Target Solution Structure

The following is the approved target structure for the migration, not a claim that these projects already exist. Keep the existing API project and project file name, `src/CoreBanking.Api/CoreBanking.Api.csproj`, and evolve it to the target .NET version and API framework. Add the other projects incrementally as required by approved specifications.

```text
CoreBanking.slnx
Directory.Build.props
Directory.Packages.props
global.json
src/
  CoreBanking.Domain/
  CoreBanking.Application/
  CoreBanking.Infrastructure/
  CoreBanking.Api/
    CoreBanking.Api.csproj       # Existing API project; retain this identity
  CoreBanking.MigrationService/
  CoreBanking.AppHost/
  CoreBanking.ServiceDefaults/
tests/
  CoreBanking.UnitTests/
  CoreBanking.FunctionalTests/
  CoreBanking.IntegrationTests/
```

### Runtime Project Responsibilities and References

```text
CoreBanking.Application ──────────────> CoreBanking.Domain
CoreBanking.Infrastructure ──────────> CoreBanking.Application
CoreBanking.Infrastructure ──────────> CoreBanking.Domain
CoreBanking.Api ─────────────────────> CoreBanking.Application
CoreBanking.Api ── composition root ─> CoreBanking.Infrastructure
CoreBanking.MigrationService ─────────> CoreBanking.Infrastructure
CoreBanking.Api ─────────────────────> CoreBanking.ServiceDefaults

CoreBanking.AppHost ── orchestrates ─> CoreBanking.Api
CoreBanking.AppHost ── orchestrates ─> CoreBanking.MigrationService

CoreBanking.UnitTests ────────────────> CoreBanking.Domain
CoreBanking.UnitTests ────────────────> CoreBanking.Application
CoreBanking.FunctionalTests ── tests ─> CoreBanking.Api
CoreBanking.IntegrationTests ─ tests ─> CoreBanking.Infrastructure / AppHost
```

Arrows denote compile-time project references except the AppHost orchestration arrows, which denote Aspire resource relationships and startup dependencies. The allowed references and responsibilities are:

| Project | Responsibility | Allowed project references |
| --- | --- | --- |
| `CoreBanking.Domain` | Banking entities/value objects, domain services, errors, and financial invariants. | None of the other solution projects. No ASP.NET Core, EF Core, HTTP, or infrastructure dependencies. |
| `CoreBanking.Application` | Use cases, application contracts/DTOs, ports, and application-level orchestration/validation. | `CoreBanking.Domain` only. |
| `CoreBanking.Infrastructure` | EF Core/SQL persistence, migrations/configuration, transactions, idempotency persistence, and the local Mambu adapter/mock. | `CoreBanking.Application` and `CoreBanking.Domain`; implement ports declared inward. |
| `CoreBanking.Api` | HTTP endpoints/contracts, authentication/authorization, transport validation/error handling, OpenAPI, and application composition. | `CoreBanking.Application`; `CoreBanking.Infrastructure` only from the composition root to register implementations; `CoreBanking.ServiceDefaults` for approved hosting defaults. Keep the existing `CoreBanking.Api.csproj` as the web entry point. |
| `CoreBanking.MigrationService` | One-shot database migration process; exits successfully only after migrations complete. | `CoreBanking.Infrastructure` and the technical hosting/configuration dependencies it needs. It contains no banking use-case rules. |
| `CoreBanking.AppHost` | Declare local SQL, API, migration, configuration, health and startup ordering resources. | Aspire hosting packages and executable project resources. It orchestrates startup; it does not implement business rules. |
| `CoreBanking.ServiceDefaults` | Reusable hosting, telemetry, and health-check registration. | Technical hosting packages only; never Domain or banking business projects. |

`CoreBanking.Api` may know about Infrastructure in `Program.cs` or an equivalent composition-root extension to wire dependency injection. API endpoints and application use cases must not depend on concrete Infrastructure types. Do not invert the arrows, introduce circular references, or place Domain under Application. Do not add a generic repository, mediator, or event bus without a demonstrated requirement.

### Test Project Responsibilities and References

Test projects are outside the runtime dependency graph. Each test project references the system under test it verifies and must not become a dependency of production projects.

| Test project | What it verifies | Project references and test boundary |
| --- | --- | --- |
| `CoreBanking.UnitTests` | Fast, deterministic tests of Domain invariants and Application use cases, including state transitions, balance/precision rules, access decisions, idempotency decisions, and error precedence. | Reference `CoreBanking.Domain` and `CoreBanking.Application`. Use controlled test doubles for ports; do not require a web host, Docker, SQL Server, or real Mambu. |
| `CoreBanking.FunctionalTests` | The complete HTTP pipeline and externally observable API behavior: routing, binding, validation, authentication/scopes, authorization, Problem Details, the 677-680 exception, status/payload/headers, and OpenAPI. | Reference the existing `CoreBanking.Api` project and use ASP.NET Core's test host/client (for example, `WebApplicationFactory`/`TestServer`). Replace external dependencies with controlled test implementations. These tests are not a substitute for SQL-backed integration tests. |
| `CoreBanking.IntegrationTests` | Important interactions with real infrastructure: Infrastructure adapters, SQL Server schema/migrations/transactions/concurrency, the local HTTP mock, and Aspire startup/resource ordering. | Reference the Infrastructure and/or AppHost projects needed by the tests and use isolated SQL Server and Aspire test resources. Do not call a real Mambu tenant or use persistent developer/workshop data. |

Keep test suites focused at their correct boundary: test routine business logic as unit tests; use functional tests for the HTTP application pipeline; reserve integration tests for critical infrastructure interactions rather than every data permutation. Tests must be isolated, repeatable, self-checking, and traceable to approved requirements and characterized legacy scenarios. Use SQL Server-backed tests for claims about SQL locking, `rowversion`, decimal precision, migration behavior, transaction atomicity, and concurrency; EF InMemory or SQLite does not prove SQL Server behavior.

## Core Principles

### I. Characterize Before Changing

- Treat the existing code, database schema, migrations, Postman collection, README, and workshop documentation as evidence of current behavior; record discrepancies rather than assuming they agree.
- Capture relevant behavior before refactoring. Preserve routes, HTTP semantics, payloads, authorization, persistence effects, and seed behavior unless an approved specification explicitly authorizes a change.
- Do not rewrite the application wholesale, remove useful legacy controls, or combine unrelated migration steps into an opaque change.
- Keep compatibility decisions and requirement-to-test traceability reviewable.

### II. Keep Business Rules Independent

- The target dependency direction is Domain <- Application <- Infrastructure and Api composition: Domain has no dependency on Application, ASP.NET Core, EF Core, HTTP, or infrastructure; Application depends on Domain, never on Api or Infrastructure; Infrastructure implements Application ports; Api depends on Application and may reference Infrastructure only at its composition root.
- Keep financial invariants and use-case decisions out of HTTP endpoints and persistence adapters. Endpoints translate HTTP and delegate; adapters perform technical work.
- Separate domain entities, persistence models, application contracts, and HTTP DTOs when the migration creates those boundaries.
- Introduce projects, abstractions, repositories, mediators, or event buses only when they serve a demonstrated boundary or requirement. Do not force a bulk extraction merely to match a diagram.

### III. Test-Driven Development and Coverage

- TDD is mandatory for every new production behavior and behavior change: write the focused test first, run it and verify that it fails for the intended reason, implement the minimum production code to pass, then refactor while keeping the tests green (Red-Green-Refactor).
- Do not write or modify production code for a new behavior before its corresponding failing test exists. A test failure caused by a broken build, missing dependency, invalid test setup, or unavailable infrastructure is not valid Red evidence.
- Characterization tests for existing behavior may initially pass; they capture the baseline and are not represented as the Red phase for new behavior. Before refactoring characterized behavior, add tests that distinguish the approved target behavior where it differs.
- The completed migration MUST achieve at least **80% line coverage for each production project**, measured across the production code in that project by the automated coverage gate. Do not rely only on a solution-wide average that can hide an under-covered production project.
- The coverage denominator includes production code in `CoreBanking.Domain`, `CoreBanking.Application`, `CoreBanking.Infrastructure`, `CoreBanking.Api` (the existing `src/CoreBanking.Api/CoreBanking.Api.csproj`), and `CoreBanking.MigrationService`. Exclude only compiler/tool-generated code that the coverage tool reliably identifies as generated; do not exclude handwritten code, endpoints, error handling, persistence adapters, migration orchestration, or difficult branches merely to pass the threshold.
- Configure CI to collect coverage for the production projects and fail the quality gate if any project is below 80%. Select and pin a coverage collector/reporting tool as part of the migration plan; until then, do not claim the coverage gate is implemented or met. Track meaningful branch and risk coverage in addition to the mandatory line-coverage threshold.
- Preserve scenario identifiers and traceability from approved requirement to test, layer, result, and revision. Do not present Postman request counts or saved reports as equivalent to automated assertions, coverage, or fresh test runs.
- Keep unit tests independent of Docker and SQL; use the real SQL Server engine for claims about SQL locking, precision, rowversion, transactions, and concurrency. SQLite or EF InMemory is not a substitute for those claims.
- Do not delete, skip, weaken, or broadly catch test failures to obtain a green result. Report unavailable tooling, prerequisites, unrun checks, and coverage gaps explicitly.

### IV. Preserve Financial and Data Invariants

- Use `decimal` for money. Preserve SQL `decimal(28,8)` storage; EUR adjustments with more than two fractional digits must be rejected, never rounded. Do not infer a `valueDate`.
- Preserve account states, transition rules, balances, holds, overdraft checks, compensating movements, and audit reasons. Account closure is allowed only for `ACTIVE` or `APPROVED` accounts with zero book balance, available balance, and holds; repeated closure of `CLOSED` remains HTTP 200, while other invalid transitions remain conflicts.
- Balance updates, movements, and idempotency records must remain atomic. Concurrent requests must not create invalid balances, partial postings, or duplicate movements.
- Preserve idempotency isolation by consumer and UUID v4 key, normalized payload comparison, conflict behavior for a different payload, `Processing` behavior, and completed replay semantics. Any approved replay-correlation change must preserve the stable business result and prevent a second posting.
- Use versioned EF migrations. Never replace them with `EnsureCreated`, regenerate the initial migration as a shortcut, drop or recreate an existing database, or reseed on every startup. Verify both empty and existing database paths and protect persistent workshop data.
- Preserve the deterministic initial seed: 5 products, 1,000 customers, and 4,000 accounts, subject to later legitimate writes.

### V. Preserve the Public API and Its Security Boundary

- Keep the base path `/open-finance/system/accounts/v1` and existing operation semantics. Runtime/framework upgrades do not imply a public API version change.
- Preserve JWT signature (HS256), issuer, audience, and expiry validation; the five scopes and their combinations; consumer-specific resource visibility; and the observed distinction between 401, 403, and 404.
- Authenticate, authorize, and check resource visibility before disclosing business state. An inaccessible or nonexistent resource remains indistinguishable as HTTP 404 where that is the existing contract.
- In the target architecture, each HTTP operation, including the local token and health operations, has its own FastEndpoints endpoint class. Endpoint configuration owns route/version/authorization metadata; handlers delegate to use cases and propagate `CancellationToken`. Do not keep MVC controllers or Minimal API operation mappings in the completed migration.
- Use FastEndpoints.Swagger for the target API documentation. Register endpoint discovery and Swagger consistently from the selected endpoint assemblies; if none contain endpoint declarations, register neither FastEndpoints nor a Swagger document/UI. Test both populated and empty endpoint hosts.
- The target general HTTP error format is RFC 9457 `application/problem+json`, with a `status` matching the actual HTTP status and no leaked secrets, SQL details, or stack traces. Preserve protocol-specific OAuth errors as characterized and document that exception.
- OpenAPI must describe actual routes, scopes, input constraints, error responses, and balance response variants. It must not advertise behavior that runtime tests do not verify.

### VI. Preserve the Accepted Balance-Error Exception

Known bug accepted as correct workshop behavior: balance inquiry business errors 677, 678, 679, and 680 MUST continue to return HTTP 200 with `application/json` and the existing `errorCode`/`message` payloads. They MUST NOT be converted to RFC 9457 Problem Details or to non-200 HTTP status codes. Preserve their messages, evaluation precedence, absence of partial balances, and authorization checks. Fixing other anti-patterns does not authorize fixing this bug. Any future correction requires a separately approved specification and compatibility decision.

The exact messages are:

- 677: `Balance is not posible`
- 678: `Balance inquiry is not allowed for a locked account`
- 679: `Balance is unavailable while the account is pending approval`
- 680: `Required balance information is missing`

Preserve evaluation precedence after access checks: blocked inquiry, `LOCKED`, `PENDING_APPROVAL`, then missing balance information. Cover all four cases with regression tests and document the 200 response variants in OpenAPI.

### VII. Make Dependencies and Runtime Reproducible

- The target is C# on `net10.0`, with an exact agreed SDK in `global.json` and compatible tooling. Until that migration is approved and completed, do not silently retarget or make claims based on an assumed SDK.
- **The migration MUST adopt NuGet Central Package Management (CPM); this is a required migration outcome, not an optional recommendation.** Create one `Directory.Packages.props` at the repository/solution root, enable `ManagePackageVersionsCentrally`, and disable `CentralPackageVersionOverrideEnabled`.
- The root `Directory.Packages.props` is the sole source of versions for direct NuGet dependencies across all projects. Declare each package's exact version there using `PackageVersion`. Every project MUST reference packages without specifying a version in its project file: no `Version` attribute, nested `<Version>` element, or `VersionOverride` on `PackageReference`. Do not add nested central catalogs, per-project exemptions, or local overrides.
- Required project-file form: `<PackageReference Include="FastEndpoints" />`. Required central form: `<PackageVersion Include="FastEndpoints" Version="8.3.0" />`. Package metadata such as `PrivateAssets` may remain on `PackageReference` when needed, but must not introduce or override a package version.
- As part of the migration, remove existing inline package versions (including those currently in `CoreBanking.Api.csproj`) and add their exact, compatibility-verified versions to the central catalog. A project must not be considered migrated while any direct NuGet package version remains declared outside the root `Directory.Packages.props`.
- The target versions of `FastEndpoints` and `FastEndpoints.Swagger` are both exactly 8.3.0. Pin SDKs, tools, images, and Aspire versions using their appropriate mechanisms; CPM does not manage them.
- Verify compatibility of other package versions before selecting them. Do not invent versions or silently upgrade required packages to unblock a restore.
- Keep build and test commands aligned with the actual project and runner. A successful build alone does not establish behavioral, architectural, or data compatibility.

### VIII. Keep the Workshop Local, Safe, and Reproducible

- Mambu and token issuance are local simulations. Keep the Mambu mock intercepted; never connect synthetic workshop credentials or data to a real tenant.
- Support only the approved `Workshop`/`Development` execution boundary. Do not add production deployment, external publishing, or live banking integrations without a separate approval.
- Never commit `.env`, secrets, signing keys, real customer data, or sensitive test reports. Do not request or print secrets; use local configuration or approved secret storage.
- The target Aspire flow is healthy SQL Server -> one-shot migration service completes successfully -> API starts. Preserve Compose and Dockerfile as documented alternatives during transition; do not run both topologies against the same ports or data simultaneously.
- Local test resources and cleanup must be isolated and explicitly identified. Do not stop or delete unrelated services, containers, volumes, or databases to free ports or reset tests.

## Governance

- This constitution is the durable project authority. Feature specifications, plans, tasks, code, and generated artifacts must remain consistent with it.
- Before implementation, inspect the current repository and relevant approved requirements. Ask for clarification when a material business or compatibility decision cannot be resolved from approved evidence; do not invent financial rules, credentials, or external endpoints.
- Every change review must state its scope, compatibility impact, affected tests, and any deviation from the characterized baseline. Data-destructive operations and external/production actions require explicit authorization.
- Amendments require a reviewed rationale, impact assessment, and an update to this file. The accepted 677-680 behavior cannot be changed through an implementation-only edit.
- Copilot instructions and C# file instructions guide contributors but do not override this constitution or approved feature specifications.
- A production change is not complete unless its required TDD evidence is recorded and the automated coverage gate confirms at least 80% line coverage for every production project. Existing baseline code that has not yet reached the threshold must be brought up to the threshold as part of the approved migration; it is not grounds to waive the gate.

**Version**: 1.2.0 | **Ratified**: 2026-11-04 | **Last Amended**: 2026-11-04

## Microsoft Learn References

- [Common web application architectures: Clean Architecture and project responsibilities](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures#clean-architecture)
- [Integration tests in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0)
- [Unit testing best practices for .NET](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)
