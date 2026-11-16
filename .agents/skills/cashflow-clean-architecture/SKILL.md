---
name: cashflow-clean-architecture
description: "Use when implementing, refactoring, testing, reviewing or documenting C#/.NET code in the CashFlow repository (Transactions or Consolidation). Enforce its Clean Architecture folder conventions, financial rules, local-only deployment scope, architecture tests and >=80% line coverage quality gates. Do not use for unrelated repositories."
---

# CashFlow: implementation standard

Apply this skill to all CashFlow implementation changes. **Repository ADRs and approved decisions are authoritative**; this skill is a practical checklist, not a replacement for them. Before changing a behavior, read `docs/architecture/mvp-decisions.md` and the relevant `docs/architecture/adr/ADR-*.md`. If an implementation conflicts with an approved decision, do not silently override it. Explain the trade-off and update the ADR only after the decision is approved.

## Repository and stack

- Two independently deployable financial services, **Transactions** and **Consolidation**, inside `src/Services/`. This `Services` directory denotes deployable applications; **do not create a business-layer `Services` folder**.
- Each service has `CashFlow.<Service>.Domain`, `Application`, `Infrastructure`, `Api`.
- C#/.NET 10, ASP.NET Core, EF Core + PostgreSQL, RabbitMQ, Keycloak OIDC, React/TypeScript, NGINX. Follow the existing code/versions rather than upgrading without a decision.
- **Current execution scope: LOCAL + Docker Compose only.** Azure/AKS DEV/HOM/PROD and cloud deployment remain future work.

## Naming and directories

```text
src/Services/<Service>/
  CashFlow.<Service>.Domain/
    Models/         # Business models and business invariants; no persistence attributes
    Enums/          # Business enumerations
    Exceptions/     # Custom typed domain exceptions derived from DomainException
  CashFlow.<Service>.Application/
    UseCases/       # Feature/use-case orchestration, e.g. CreateTransaction/
    Interfaces/     # Ports as needed, created only when concrete contracts exist
  CashFlow.<Service>.Infrastructure/
    Entities/       # EF Core persistence entities ONLY
    ...             # DB mappings, repositories, broker implementations, etc. as needed
  CashFlow.<Service>.Api/
    ...             # ASP.NET Core transport and composition root
```

- **No `Domain/Entities`, `Domain/ValueObjects`, or `Domain/Services`.** Put domain data and rules in `Domain/Models`. Organize other real domain types only when needed; do not create empty placeholders.
- **`Entities` means infrastructure persistence types only.** A domain `Models/Transaction.cs` is a different responsibility from an eventual `Infrastructure/Entities/TransactionEntity.cs`; define explicit mapping in Infrastructure.
- Namespace mirrors the path, for example `CashFlow.Transactions.Domain.Models` and `CashFlow.Transactions.Domain.Exceptions`.
- `Application/UseCases/<Action>/` contains each use case. Do not invent business `Services`, generic managers or framework-specific handlers in Domain.
- Exceptions represent business rule failures, not HTTP status codes; API maps these to `ProblemDetails` where appropriate.
- Do not introduce external framework dependencies or `PackageReference` into Domain or Application. Application may reference only its own Domain project. Domain has no project references.
- Infrastructure may depend on Domain/Application, never Api. Api composes dependencies; it must not contain core financial rules.
- Transactions and Consolidation cannot have compile-time references to each other's internals or query each other's logical databases.

## Financial invariants: already decided

- Currency BRL, .NET `decimal`; EF PostgreSQL `numeric(19,2)`. Positive input, maximum two decimal places; credit/debit defines sign. No float/double for money.
- `reference_date` is a `DateOnly` business date. Validate 'today' in `America/Sao_Paulo`; current/past allowed without retroactive window, future rejected. Timestamps UTC.
- Segregate all data by `merchant_id`; do not trust merchant IDs provided by clients without verifying membership.
- Authorization: Keycloak validates identity; CashFlow verifies memberships/roles. Fail closed if reliable authorization is unavailable; do not bypass auth for convenience.
- Transaction, idempotency result, audit record and Outbox entry must commit atomically when implemented. Return 201 after local commit, not after Consolidation.
- Consolidation consumes versioned events idempotently, stores daily totals and ProcessedEvent atomically. Closing balance is **calculated on read**, not stored as authoritative balance. No synchronous dependency on Consolidation during transaction registration.
- HTTP creation requires `Idempotency-Key`; uphold approved multimerchant and immutable-history rules.
- Do not implement out-of-MVP reversal/edit/delete as an accidental shortcut.

For detailed or disputed behavior consult `docs/architecture/mvp-decisions.md`, contracts, ADRs, and the relevant implementation checklist instead of inferring.

## Testing and architectural gates

- Use xUnit for unit/architecture tests and `NetArchTest.Rules` plus assembly / project reference checks for architectural enforcement. Keep test assemblies outside Domain and Application.
- Maintain tests in `tests/`. For every change, add/update tests for happy paths, invalid values, boundary dates, exceptions, isolation, idempotency/concurrency where relevant.
- Require **at least 80% line coverage in each production module under test**, not a global average that can conceal under-tested modules. Use Coverlet; add independent gate when new Application/Consolidation code appears. Never treat a placeholder test as meaningful coverage.
- Existing Transactions Domain command (PowerShell):

```powershell
dotnet test tests/CashFlow.Transactions.UnitTests/CashFlow.Transactions.UnitTests.csproj -c Release /p:CollectCoverage=true /p:Threshold=80 /p:ThresholdType=line /p:ThresholdStat=Minimum '/p:Include=[CashFlow.Transactions.Domain]*' /p:CoverletOutputFormat=cobertura
```

- Execute architecture and build validation:

```powershell
dotnet restore CashFlow.slnx
dotnet build CashFlow.slnx -c Release
dotnet test tests/CashFlow.ArchitectureTests/CashFlow.ArchitectureTests.csproj -c Release
```

- Where implementation requires local dependencies, use `docker compose up --build -d` and test without Azure. Never present a health response as proof of end-to-end business operation.
- CI workflow `.github/workflows/local-build.yml` should fail on architecture/unit/coverage failures. Protected-branch required checks need GitHub branch ruleset and must be verified separately.
- Report exactly what was implemented, commands actually executed, passing/failing counts and measured coverage. **Do not claim successful tests or >=80% coverage without executed output.**

## Workflow for every implementation task

1. Read scope, approved requirements, relevant ADRs and actual source files before editing.
2. Choose correct ownership: business invariant -> Domain/Models; orchestration -> Application/UseCases; persistence integration -> Infrastructure/Entities and adapters; HTTP -> Api.
3. Implement smallest coherent behavior with typed domain exceptions, no framework leaks to inner layers.
4. Write/update unit and architectural tests. Add coverage gates for every newly implemented module.
5. Run build, tests, coverage and relevant local integration checks. Capture evidence in `docs/architecture/validations/` when executed.
6. Update only documentation/diagrams/contracts affected by the change. Clearly distinguish pending validation from completed evidence.
7. Summarize files changed, validations and remaining risks. Do not provision or deploy Azure.

## Sources of truth

- `docs/architecture/mvp-decisions.md`
- `docs/implementation/domain-conventions.md`
- `docs/implementation/quality-gates.md`
- `docs/architecture/mvp-implementation-checklist.md`
- `docs/architecture/adr/`
- `docs/architecture/contracts/` (proposed contracts still need functional validation)
