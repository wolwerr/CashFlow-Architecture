# CashFlow coding agent guidance

This repository follows approved C#/.NET 10 Clean Architecture conventions.

For **any** implementation, refactoring or testing of CashFlow code, use the repository skill at `.agents/skills/cashflow-clean-architecture/SKILL.md`. Read `docs/architecture/mvp-decisions.md` and the relevant ADRs before changing architecture or business rules.

Key constraints: `Domain/Models` (never Domain/Entities), `Domain/Enums`, `Domain/Exceptions`, `Application/UseCases` (no business Services), `Infrastructure/Entities` only for persistence; no framework packages in Domain or Application. Keep Transactions and Consolidation independent. Enforce architecture tests and >=80% per-module line coverage. LOCAL Docker Compose only; Azure is future work.

Do not report any validation as passed unless it was run and its output inspected.
