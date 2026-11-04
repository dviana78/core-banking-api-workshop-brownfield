---
description: "Use when creating or editing C# source in the Core Banking API, including SOLID design, dependency injection, its legacy ASP.NET Core implementation, and the planned .NET 10 modernization. Covers architecture, financial invariants, API compatibility, persistence, and tests."
name: "Core Banking C# Guidelines"
applyTo: "src/**/*.cs, tests/**/*.cs"
---

# Core Banking C# Guidelines

## Work With the Actual Project State

- The current source belongs to the single `src/CoreBanking.Api/CoreBanking.Api.csproj` Web SDK project targeting `net8.0`. It enables nullable reference types and implicit usings and currently uses MVC controllers, EF Core, and Swashbuckle.
- The existing source folders are `Controllers`, `Data`, `Integration`, and `Models`. Do not describe them as Clean Architecture layers or assume separate test/domain/infrastructure projects already exist.
- The workshop target is `net10.0` and separates Domain, Application, Infrastructure, and Api, with FastEndpoints 8.3.0. Migrate incrementally under an approved specification; do not retarget the project or perform a bulk rewrite as an incidental C# change.
- Preserve the surrounding code's formatting and naming unless the scoped change requires otherwise. Do not reformat unrelated legacy code.

## C# and Layering

- Keep nullable analysis enabled. Represent absence explicitly with nullable types where appropriate; validate untrusted input at the boundary rather than suppressing warnings or inventing defaults.
- Prefer clear, strongly typed domain concepts and explicit mappings. Do not pass EF entities or HTTP request/response DTOs across architectural boundaries.
- In the target design, Domain contains framework-independent invariants; Application orchestrates use cases and defines ports; Infrastructure implements SQL/EF and mock-provider adapters; Api handles HTTP, authentication/authorization, validation, and composition.
- Keep HTTP handlers thin and delegate to use cases. Keep business decisions out of controllers/endpoints, `DbContext`, and provider adapters. Do not add generic abstractions, service locators, mediators, or event buses without a demonstrated need.
- Use asynchronous APIs for I/O, propagate the caller's `CancellationToken` throughout, and do not block on tasks with `.Result`, `.Wait()`, or equivalent sync-over-async.
- Keep secrets, connection strings, signing keys, and real customer data out of source, tests, logs, and reports.

## SOLID Design Principles

Apply SOLID to improve cohesion, testability, and change isolation in the existing application and its target architecture. Apply the principles proportionately: do not split every method into a class, add an interface for every type, or build extension points for hypothetical requirements.

- **Single Responsibility Principle (SRP):** Give each class and method one cohesive responsibility and one primary reason to change. Keep HTTP translation in Api, use-case coordination in Application, financial invariants in Domain, and SQL/HTTP-provider details in Infrastructure. Split a type when it combines responsibilities that change for independent reasons; do not use line count alone as the test.
- **Open/Closed Principle (OCP):** When an approved requirement introduces meaningful variants, make the behavior extensible behind a stable, narrow contract so a new variant does not require editing unrelated business rules. Prefer composition and explicit strategies over conditionals spread across layers. Do not create speculative plugin systems or public extension points without a real consumer and compatibility need.
- **Liskov Substitution Principle (LSP):** Implementations of a base type or interface must preserve its documented preconditions, postconditions, invariants, and error semantics. A substitute repository or Mambu mock must not silently weaken atomicity, authorization, idempotency, cancellation, or result guarantees. Prefer composition over inheritance when substitutability cannot be stated and tested clearly.
- **Interface Segregation Principle (ISP):** Define small, cohesive Application-owned ports around the needs of a use case or capability. Do not make consumers depend on unrelated operations, and do not create broad interfaces such as a catch-all banking service or generic repository when focused contracts are clearer.
- **Dependency Inversion Principle (DIP):** Keep high-level Domain and Application policy independent of Infrastructure details. Declare required ports inward (normally in Application), implement them in Infrastructure, and wire implementations at the Api or MigrationService composition root. Pass collaborators through explicit constructor injection; do not instantiate infrastructure dependencies inside use cases, use a service locator, or let Domain reference Infrastructure.
- Use abstractions where they protect a real boundary, enable a required substitution, or make behavior testable. Keep simple, stable implementation details concrete; abstraction without a meaningful consumer adds indirection rather than decoupling.

## Dependency Injection and Resource Lifetimes

- Register services at the composition root and resolve dependencies through constructor injection. Keep service constructors explicit; do not resolve services from `IServiceProvider` inside business logic.
- Choose `Transient`, `Scoped`, or `Singleton` lifetimes according to state and resource ownership. Treat EF Core `DbContext` and request-bound work as scoped; never capture a scoped service in a singleton.
- Singleton services must be thread-safe and must not retain request-specific or mutable banking state. Do not use static mutable state as a substitute for dependency injection.
- Let the container dispose services it creates. Do not manually dispose injected dependencies.

## Banking and Compatibility Invariants

- Use `decimal`, never binary floating point, for monetary values. Preserve SQL `decimal(28,8)`; reject EUR adjustments with more than two fractional digits instead of rounding, and do not infer `valueDate`.
- Preserve account states, permitted transitions, available/book balances, holds, overdraft checks, compensating movements, and audit reasons. Closure requires `ACTIVE` or `APPROVED`, zero balances, and no holds; a repeated close of `CLOSED` remains HTTP 200.
- Preserve atomicity between balance changes, movement records, and idempotency records. Respect SQL uniqueness, foreign keys, rowversion/locking, and existing concurrency behavior. Never implement a fallback that can produce partial or duplicate postings.
- Preserve idempotency by consumer and UUID v4 key, normalized payload comparison, conflict handling for a different payload, processing behavior, and stable replay results. An approved correlation fix must not mutate the original record or post a second movement.
- Keep EF migrations versioned and non-destructive. Do not use `EnsureCreated`, rewrite the initial migration to hide schema changes, seed on every startup, or delete/recreate an existing database. Preserve the initial deterministic seed.
- Preserve JWT validation, required scopes, consumer-specific resource visibility, and access-check ordering. Do not disclose whether a hidden resource has a blocked balance or other business state.

## HTTP and Workshop Exceptions

- Preserve the base route `/open-finance/system/accounts/v1`, existing operation contracts, and characterized OAuth error behavior unless an approved feature specification authorizes a change.
- In the target API, use one FastEndpoints endpoint class per HTTP operation, including token and health operations. Put route/version/auth configuration in endpoint configuration, delegate business work, and do not retain MVC or Minimal API operation mappings after migration.
- Use RFC 9457 `application/problem+json` for general HTTP errors in the target; the actual HTTP status must match the Problem Details `status`. Do not leak implementation details.
- **Mandatory exception:** balance errors 677-680 remain HTTP 200 with `application/json` and the exact existing `errorCode` and `message`. Do not wrap them in Problem Details, change their status or wording, or return partial balances. Preserve access checks and precedence: inquiry blocked, `LOCKED`, `PENDING_APPROVAL`, missing balance information.
- Keep Mambu as a local mock. Never point synthetic workshop traffic at a real Mambu tenant.

## Testing Expectations

- For new behavior or behavior changes, add and run a focused test before implementation; verify an expected failure for the intended reason, then implement, rerun, refactor, and run the relevant regression.
- Green characterization tests are valid evidence of pre-existing behavior but do not count as a Red phase for new behavior.
- Test boundary conditions, authorization-before-business ordering, persisted effects, and error payloads, not just status codes.
- Use SQL Server-backed integration tests for SQL precision, locking, rowversion, transactions, migrations, and concurrency. In-memory providers do not prove these behaviors.
- Keep tests isolated and deterministic; do not rely on request ordering, mutable shared accounts, external services, or real credentials.

## Microsoft Learn References

- [Write extensible code: SOLID principles](https://learn.microsoft.com/dynamics365/fin-ops-core/dev-itpro/extensibility/writing-extensible-code#proven-principles)
- [Architectural principles: separation of concerns and dependency inversion](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/architectural-principles#common-design-principles)
- [Dependency injection guidelines for .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines)
