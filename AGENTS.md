# HealthManager (backend)

## Spec-driven development

Canonical specs in `spec/` — read before making changes to any domain entity, state machine, business rule, or API endpoint.

| Spec | Content |
|------|---------|
| `spec/entities.yaml` | Entity definitions (all fields, types, constraints) |
| `spec/state-machines.yaml` | State transitions for Appointment, Receivable, ConfirmationStatus |
| `spec/business-rules.yaml` | Tenant isolation, auth, authorization, scheduling, finance, documents, outbox |
| `spec/auth-flow.yaml` | Login, refresh, logout, change-password, patient-portal auth, JWT structure |
| `spec/api-endpoints.yaml` | Every API route with method, auth policy, request/response summary |

Update the spec first, then code to match. When spec and code disagree, the spec is the bug.

.NET 10 (`net10.0`) modular monolith — CRM médico multi-tenant, Brazil-first (`pt-BR`, CPF/phone BR, BRL, clinic timezone).

## Two repos

| Repo | Path |
|------|------|
| Backend (this one) | `C:\Users\Marcus Nogueira\Documents\HealthManager` |
| Frontend | `C:\Users\Marcus Nogueira\Documents\healthmanager-web` |

OpenAPI contract at `docs/openapi.json` — update it when changing request/response, then regenerate frontend client (`npm run generate:api` in frontend repo). Manual testing via `src/HealthManager.Api/HealthManager.Api.http`.

## Commands

```powershell
dotnet restore HealthManager.sln
dotnet build HealthManager.sln
dotnet test HealthManager.sln
dotnet test HealthManager.sln --filter "FullyQualifiedName~AppointmentsEndpoints"
docker compose up --build                                                                   # API at :8080, Swagger at /swagger
dotnet test HealthManager.sln --collect:"XPlat Code Coverage" --results-directory ./TestResults
dotnet tool run reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"CoverageReport" -reporttypes:"Html;Cobertura;MarkdownSummaryGithub"
dotnet tool run dotnet-ef migrations add <Nome> --project src/HealthManager.Infrastructure --startup-project src/HealthManager.Api --context HealthManager.Infrastructure.Persistence.AppDbContext --output-dir Persistence/Migrations
```

`dotnet` may not be in PATH — fallback: `C:\Program Files\dotnet\dotnet.exe`.

Local tools in `.config/dotnet-tools.json`: `dotnet-ef` (v10.0.9), `reportgenerator` (v5.5.10).

## Projects

| Project | Role | References |
|---------|------|-----------|
| **Api** | REST, auth, Swagger, middlewares | Application, Infrastructure |
| **Worker** | Outbox processing, notifications | Application, Infrastructure |
| **Lambda** | Outbox processing for AWS deploy (`AssemblyName=bootstrap`, custom runtime) | Application, Infrastructure |
| **Application** | Use cases, DTOs, validations, rules | Domain |
| **Infrastructure** | EF Core, JWT, storage, integrations | Application, Domain |
| **Domain** | Entities, enums, core interfaces | — |

## Architecture

- Modular monolith — wired via `AddApplication()` + `AddInfrastructure()` in `Program.cs`
- Tenant isolation via `clinic_id` on `TenantEntity` — EF Core query filters on every tenant entity. PlatformAdmin bypasses via `BypassTenantFilter`.
- Soft delete via `DeletedAt` — global query filter on every entity
- Finance uses `receivables + payments`; partial payments tracked via `ReceivedAmount` on `Receivable`. Separate `ExpenseService`.
- Outbox: `OutboxEvent` entity, `OutboxProcessor` batch size 25, `OutboxWorker` polls every 15s
- Database auto-migrates on startup (or `EnsureCreatedAsync` for InMemory)
- `Program.cs` is `partial class Program` — required for `WebApplicationFactory<Program>` in integration tests
- Error handling maps exceptions to HTTP status: `InvalidOperationException` → 400, `UnauthorizedAccessException` → 401, `KeyNotFoundException` → 404, else 500
- `Directory.Build.props`: `TreatWarningsAsErrors=false`, nullable enabled, implicit usings, central TFM `net10.0`. Individual `.csproj` files must NOT set `<TargetFramework>`.
- `global.json` pins SDK `10.0.301` with `rollForward: latestMajor`
- Roles: `PlatformAdmin`, `Admin`, `Secretary`, `Doctor`, `Patient`. Auth policies: `PlatformAdminOnly`, `ClinicAdminOrSecretary`, `ClinicStaff`, `PatientPortal`
- PatientPortal auth: login via `CPF + PatientAccessToken` (separate JWT lifetime, no refresh tokens)
- `X-Clinic-Id` header accepted as tenant override (falls back to JWT `clinic_id` claim)
- No Serilog, no FluentValidation — `Microsoft.Extensions.Logging` + `System.ComponentModel.DataAnnotations`. Boundary interfaces only: `IApplicationDbContext`, `IPasswordHasher`, `IJwtTokenService`, `IOutboxService`, `IStorageService`. Services registered as concrete classes.
- Worker has its own `appsettings.json` / `appsettings.Production.json`
- No `.env.*` files. Settings via env vars or `appsettings.json`.

## Testing

- Integration: each test class creates `new ApiTestFactory()` — EF Core InMemory, `FakeStorageService` singleton, seeded DB per class. Sets `USE_INMEMORY_DATABASE=true` + `SENTRY_DSN=""` + `Testing` env (bypasses HTTPS redirect).
- Unit: use `FakeTenantProvider`, `FakeStorageService` from `TestDoubles.cs` + `TestHelpers.CreateDbContext()` (fresh InMemory DB per test).
- Stack: xUnit + FluentAssertions + `Microsoft.AspNetCore.Mvc.Testing` + EF Core InMemory + `coverlet.collector`
- `ApiTestFactory` exposes `LoginAsync()`, `LoginWithSessionAsync()`, `CreateAuthenticatedClientAsync()`, `WithDbContextAsync()`, `SeedSecondClinicPatientAsync()`
- Integration tests: `tests/HealthManager.Tests/Integration/` — 9 endpoint test classes covering all major controllers

## Seed data

| Credential | Email | Password |
|-----------|-------|----------|
| PlatformAdmin | `platform@healthmanager.local` | `ChangeMe123!` |
| Clinic Admin | `admin@clinicaaurora.com` | `ChangeMe123!` |

Seed IDs: `clinicId=11111111-1111-1111-1111-111111111111`, `platformAdminId=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa`, `clinicAdminId=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb`, `doctorId=cccccccc-cccc-cccc-cccc-cccccccccccc`, `patientId=dddddddd-dddd-dddd-dddd-dddddddddddd`, `appointmentId=eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee`, `receivableId=ffffffff-ffff-ffff-ffff-ffffffffffff`.

Integration tests reference these GUIDs directly.

## CI / Deploy

- CI builds `--configuration Release`, runs tests with coverage, uploads artifacts
- Deploy (master/main pushes only): builds Lambda `--self-contained --runtime linux-x64`, builds/pushes API Docker image to ECR, deploys to ECS Fargate, updates Lambda function code
- AWS OIDC auth via `configure-aws-credentials` with `role-to-assume: ${{ secrets.AWS_DEPLOY_ROLE_ARN }}`
- Terraform in `infra/` targets AWS (ECS Fargate + RDS + CloudFront + Lambda)
