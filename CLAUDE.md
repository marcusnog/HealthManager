# HealthManager Backend

## Codebase Overview

HealthManager is a .NET 10 modular monolith for a multi-tenant Brazilian medical CRM. The REST/SignalR API, background Worker, and AWS Lambda share Application, Domain, and Infrastructure projects backed by EF Core/PostgreSQL, with AWS ECS, RDS, S3, CloudFront, and Lambda deployment.

**Stack**: ASP.NET Core 10, EF Core 10, PostgreSQL, JWT, SignalR, xUnit, Docker, Terraform, and AWS.

**Structure**: `src/` contains three executable hosts (`Api`, `Worker`, `Lambda`) and three shared projects (`Application`, `Domain`, `Infrastructure`); `spec/` is the canonical product contract, `tests/` contains service and HTTP integration tests, `infra/` contains Terraform, and `docs/openapi.json` is the frontend contract.

For detailed architecture, data flows, dependencies, gotchas, and navigation, see [docs/CODEBASE_MAP.md](docs/CODEBASE_MAP.md).

## Source of Truth

Read [AGENTS.md](AGENTS.md) before making changes. Update the canonical files in `spec/` before changing entities, state machines, business rules, authentication, or endpoints. When request/response contracts change, update `docs/openapi.json` and regenerate the frontend client in `..\healthmanager-web`.

`AGENTS.md` supersedes historical README notes and is the authoritative operational guide for commands, seed data, testing, and deployment.
