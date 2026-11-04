# Core Banking API Project Instructions

## Philosophy

- Write all new or modified source code, code comments, identifiers, and project documentation in English. Preserve existing non-English content unless the task explicitly requires translating or replacing it.
- Reply in the language used by the user for the current request. For example, answer in Spanish when asked in Spanish, English when asked in English, and French when asked in French.
- Before starting a requested operation or substantive task, state the estimated confidence as a percentage. Proceed only when confidence is at least 95%. If confidence is below 95%, ask concise, specific clarifying questions and wait for the user's answers before proceeding.
- Base the confidence estimate on the clarity of the request, available repository evidence, and known constraints. Do not hide material uncertainty behind an unsupported percentage.

## Authority and Brownfield Workflow

- Follow [.specify/memory/constitution.md](../.specify/memory/constitution.md) for binding project rules.
- Before changing behavior or architecture, inspect the relevant code and the approved requirements. Use [docs/taller-speckit-brownfield.md](../docs/taller-speckit-brownfield.md), [README.md](../README.md), [docs/antipatrones.md](../docs/antipatrones.md), the original contract, migrations, and Postman collection as evidence; note conflicts instead of silently choosing one source.
- Treat the repository as a brownfield migration. Characterize and test existing behavior first, distinguish current implementation from target design, and make small, reviewable increments. Do not overwrite unrelated work or make broad formatting-only changes.
- Preserve existing routes, data, and behavior unless an approved specification authorizes a change. The balance errors 677-680 are an explicit exception: keep their exact HTTP 200 `application/json` payloads and messages.
- Do not invent financial rules, external endpoints, credentials, or compatibility decisions. Ask when an important decision is not resolved by approved project evidence.

## Repository and API Baseline

- The current API is one ASP.NET Core Web SDK project: `src/CoreBanking.Api/CoreBanking.Api.csproj`, targeting `net8.0` with nullable reference types and implicit usings enabled.
- The current project is organized under `Controllers`, `Data`, `Integration`, and `Models`. `Program.cs` composes the web host, EF Core SQL Server, local JWT, Mambu mock handler, middleware, and Swashbuckle. This is the legacy state, not the target architecture.
- The target described by the workshop is an incremental move to `net10.0`, Domain/Application/Infrastructure boundaries, centrally managed NuGet versions, FastEndpoints and FastEndpoints.Swagger 8.3.0, test projects, and Aspire. Do not assume those target projects or tools exist; inspect the repository before choosing commands or adding files.
- Preserve the `CoreBanking.Api` entry project and `/open-finance/system/accounts/v1` base route unless an approved specification says otherwise. Do not change the public API version merely because the .NET runtime changes.
- Keep Mambu and token issuance local mocks. Do not add live Mambu connectivity, production deployment, or use of real banking data.

## Architecture and Implementation

- Move toward the constitution's dependency direction incrementally. Domain owns financial invariants; Application owns use cases and ports; Infrastructure implements technical adapters; Api owns HTTP concerns and composition.
- Keep endpoints/controllers thin during transition. Do not put business rules in HTTP handlers or persistence adapters. Do not expose `BankingDbContext` or concrete Mambu clients to use cases/endpoints.
- Separate domain, persistence, application, and HTTP models when introducing the target boundaries; map explicitly and preserve the existing database schema and contract.
- Propagate cancellation through asynchronous request, use-case, EF Core, and HTTP adapter calls. Avoid sync-over-async.

## Compatibility, Security, and Data

- Preserve authentication and authorization behavior: HS256 signature, issuer, audience, expiry, scopes, consumer isolation, resource visibility, and 401/403/404 distinctions.
- Check access before business-state disclosure. Do not expose secrets, SQL details, stack traces, or sensitive workshop data in responses, logs, OpenAPI, or reports.
- Use `decimal` for money, preserve SQL `decimal(28,8)`, reject EUR adjustments exceeding two fractional digits without rounding, and do not infer `valueDate`.
- Preserve idempotency, transaction atomicity, concurrency controls, account-state transitions, holds, overdraft rules, audit reason, seed data, and migration history. Do not use `EnsureCreated`, destructive resets, or startup reseeding as shortcuts.
- Apply RFC 9457 Problem Details to general HTTP errors in the target, but retain the characterized OAuth protocol errors and the constitutional HTTP 200 exception for balance errors 677-680.

## Dependencies and Verification

- For the target, use one root `Directory.Packages.props`, exact central versions, no per-reference `Version`/`VersionOverride`, and FastEndpoints/FastEndpoints.Swagger 8.3.0. Do not claim this is already configured; verify the current project files first.
- Write focused tests before implementing new behavior or changing behavior. For characterization, an initially passing test is acceptable evidence of legacy behavior, not a Red phase. Run the focused test, relevant regression, and applicable build; report checks that could not be run.
- The current API build command is `dotnet build src/CoreBanking.Api/CoreBanking.Api.csproj -c Release`. Use it only when compatible with the installed SDK and current change; check for actual test projects and runner configuration before proposing `dotnet test`.
- Integration claims about SQL precision, locking, rowversion, migration safety, or transaction atomicity require SQL Server-backed verification. Newman is a complementary regression aid, not proof that all Postman requests are asserted tests.
- Compose and Aspire must not run against the same ports or data simultaneously. Do not stop or delete unrelated services, containers, volumes, or databases to make tests pass.
