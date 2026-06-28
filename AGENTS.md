# HealthManager (backend)

.NET 10 (`net10.0`) modular monolith — CRM médico multi-tenant.

## Two repos

| Repo | Path |
|------|------|
| Backend (this one) | `C:\Users\Marcus Nogueira\Documents\HealthManager` |
| Frontend | `C:\Users\Marcus Nogueira\Documents\healthmanager-web` |

## Commands

```powershell
dotnet restore HealthManager.sln
dotnet build HealthManager.sln
dotnet test HealthManager.sln
dotnet test HealthManager.sln --filter "FullyQualifiedName~AppointmentsEndpoints"          # single test class
docker compose up --build                                                                   # API at :8080, Swagger at /swagger
dotnet test HealthManager.sln --collect:"XPlat Code Coverage" --results-directory ./TestResults
dotnet tool run reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"CoverageReport" -reporttypes:"Html;Cobertura;MarkdownSummaryGithub"
dotnet tool run dotnet-ef migrations add <Nome> --project src/HealthManager.Infrastructure --startup-project src/HealthManager.Api --context HealthManager.Infrastructure.Persistence.AppDbContext --output-dir Persistence/Migrations
```

`dotnet` may not be in PATH — fallback: `C:\Program Files\dotnet\dotnet.exe`.

Local tools in `.config/dotnet-tools.json`: `dotnet-ef`, `reportgenerator`.

CI (`dotnet-version: 10.0.x`) builds with `--configuration Release` and also publishes Lambda with `--self-contained --runtime linux-x64`.

## Projects

| Project | Role | References |
|---------|------|-----------|
| **Api** | REST, auth, Swagger, middlewares | Application, Infrastructure |
| **Worker** | Outbox processing, notifications | Application, Infrastructure |
| **Lambda** | Outbox processing for AWS deploy | Application, Infrastructure |
| **Application** | Use cases, DTOs, validations, rules | Domain |
| **Infrastructure** | EF Core, JWT, storage, integrations | Application, Domain |
| **Domain** | Entities, enums, core interfaces | — |

## Architecture

- Modular monolith (not microservices) — wired via `AddApplication()` + `AddInfrastructure()` in `Program.cs:37-38`
- Tenant isolation via `clinic_id` on every entity, enforced by EF Core query filters in `AppDbContext.cs:80-93`
- Soft delete via `DeletedAt` — global query filter on every entity
- Finance uses `receivables + payments` (partial payments via `ReceivedAmount` on Receivable)
- Dates in UTC; display in clinic timezone
- Outbox + Worker: `OutboxEvent` entity, Worker polls every 15s
- `Program.cs` is `partial class Program` — required for `WebApplicationFactory<Program>` in integration tests
- `Directory.Build.props`: `TreatWarningsAsErrors=false`, `LangVersion=12.0`, nullable enabled, implicit usings, central TFM (`net10.0`). Individual `.csproj` files must NOT set `<TargetFramework>` — only `Directory.Build.props` defines it.
- `global.json` pins SDK `10.0.301` with `rollForward: latestMajor`
- Brazil-first: `pt-BR`, CPF/phone BR, BRL, clinic timezone

## Testing

- Integration: each test class creates `new ApiTestFactory()` — EF Core InMemory, `FakeStorageService`, seeded DB per class. `ApiTestFactory` sets `USE_INMEMORY_DATABASE=true` + `SENTRY_DSN=""` + `Testing` env (bypasses HTTPS).
- Unit: use `FakeTenantProvider`, `FakeClock`, `FakeStorageService` from `TestDoubles.cs`
- Stack: xUnit + FluentAssertions + `Microsoft.AspNetCore.Mvc.Testing` + EF Core InMemory
- `ApiTestFactory` exposes `LoginAsync()` and `CreateAuthenticatedClientAsync()` helpers
- `FakeStorageService` is an in-memory `Dictionary<string, byte[]>` — register `IStorageService` singleton in tests
- Unit tests for `OutboxProcessor` (Services.cs:194) and `WhatsAppWebhookService` (Services.cs:997) use `AppDbContext` + InMemory DB directly, following the same pattern as `PatientServiceTests`

## Seed data

| Credential | Email | Password |
|-----------|-------|----------|
| PlatformAdmin | `platform@healthmanager.local` | `ChangeMe123!` |
| Clinic Admin | `admin@clinicaaurora.com` | `ChangeMe123!` |

Seed IDs: `clinicId=11111111-1111-1111-1111-111111111111`, `doctorId=cccccccc-cccc-cccc-cccc-cccccccccccc`, `patientId=dddddddd-dddd-dddd-dddd-dddddddddddd`, `appointmentId=eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee`, `receivableId=ffffffff-ffff-ffff-ffff-ffffffffffff`.

Used in `HealthManager.Api.http` and `docs/local-smoke-test.md`. Integration tests reference these GUIDs directly.

## Environment variables

| Variable | Required? | Default |
|----------|-----------|---------|
| `DATABASE_URL` | No (dev) | `Host=localhost;Port=5432;Database=healthmanager;Username=postgres;Password=postgres` |
| `JWT_SECRET` | Yes (prod) | `change-me-super-secret-key-32-bytes` |
| `JWT_ISSUER` | No | `healthmanager` |
| `JWT_AUDIENCE` | No | `healthmanager-web` |
| `JWT_ACCESS_TOKEN_MINUTES` | No | `30` |
| `JWT_REFRESH_TOKEN_DAYS` | No | `30` |
| `CORS_ORIGINS` | No | `http://localhost:3000` |
| `SUPABASE_URL`/`KEY`/`BUCKET` | No | Without them, document upload stores metadata only |
| `SENTRY_DSN` | No | Optional, enables Sentry |
| `WHATSAPP_*` | No | WhatsApp webhook |

`X-Clinic-Id` header accepted as tenant override (falls back to JWT `clinic_id` claim).

## API contract

`docs/openapi.json` — when changing request/response, update it and regenerate frontend client (`npm run generate:api` in frontend repo).

Manual testing via `src/HealthManager.Api/HealthManager.Api.http`.

## Infra

Terraform in `infra/` targets AWS (ECS Fargate + RDS + CloudFront + Lambda). CI/CD at `.github/workflows/backend-ci.yml` pushes ECR images and updates ECS service. Lambda builds `--self-contained --runtime linux-x64`.
