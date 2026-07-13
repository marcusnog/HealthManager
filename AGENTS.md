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

Local tools in `.config/dotnet-tools.json`: `dotnet-ef` (v10.0.9), `reportgenerator` (v5.5.10). Coverage via `coverlet.collector` (test csproj).

CI (`dotnet-version: 10.0.x`) builds `--configuration Release`, publishes Lambda `--self-contained --runtime linux-x64`, deploys API image to ECS Fargate, Lambda zip to AWS Lambda. Uses OIDC auth with AWS. Deploy runs only on master/main pushes.

No Serilog, no FluentValidation — built-in `Microsoft.Extensions.Logging` + `System.ComponentModel.DataAnnotations`. No service-layer interfaces (services registered as concrete classes in DI). Boundary interfaces (`IApplicationDbContext`, `IPasswordHasher`, `IJwtTokenService`, `IOutboxService`) remain for layer separation. `IClock` removed — inline `DateTimeOffset.UtcNow`.

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

- Modular monolith (not microservices) — wired via `AddApplication()` + `AddInfrastructure()` in `Program.cs`
- Tenant isolation via `clinic_id` on `TenantEntity` — enforced by EF Core query filters on every tenant entity (`AppDbContext.cs`). PlatformAdmin bypasses the filter.
- Soft delete via `DeletedAt` — global query filter on every entity
- Finance uses `receivables + payments`; partial payments tracked via `ReceivedAmount` on `Receivable`
- Dates in UTC; display in clinic timezone
- Outbox + Worker: `OutboxEvent` entity, `OutboxWorker` polls every 15s (`BackgroundService`, `Task.Delay(15s)`)
- `Program.cs` is `partial class Program` — required for `WebApplicationFactory<Program>` in integration tests
- `Directory.Build.props`: `TreatWarningsAsErrors=false`, nullable enabled, implicit usings, central TFM (`net10.0`). Individual `.csproj` files must NOT set `<TargetFramework>`.
- `global.json` pins SDK `10.0.301` with `rollForward: latestMajor`
- Brazil-first: `pt-BR`, CPF/phone BR, BRL, clinic timezone
- PatientPortal auth: login via `CPF + PatientAccessToken` (separate JWT lifetime, not refresh tokens)
- `X-Clinic-Id` header accepted as tenant override (falls back to JWT `clinic_id` claim)
- Worker has its own `appsettings.json` / `appsettings.Production.json` (src/HealthManager.Worker/)
- No `.env.*` files exist in this repo (README is stale). Settings via environment variables or `appsettings.json`.
- `AppDbContextFactory` (design-time) in `AppDbContext.cs` for EF Core CLI migrations

## Testing

- Integration: each test class creates `new ApiTestFactory()` — EF Core InMemory, `FakeStorageService`, seeded DB per class. `ApiTestFactory` sets `USE_INMEMORY_DATABASE=true` + `SENTRY_DSN=""` + `Testing` env (bypasses HTTPS at `Program.cs:68-71`).
- Unit: use `FakeTenantProvider`, `FakeStorageService` from `TestDoubles.cs` + `TestHelpers.CreateDbContext()` (fresh InMemory DB per test).
- Stack: xUnit + FluentAssertions + `Microsoft.AspNetCore.Mvc.Testing` + EF Core InMemory + `coverlet.collector`
- `ApiTestFactory` exposes `LoginAsync()` and `CreateAuthenticatedClientAsync()` helpers
- `FakeStorageService` is an in-memory `Dictionary<string, byte[]>` — registered as `IStorageService` singleton
- `AppDbContext` creation in tests via `new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())`

## Seed data

| Credential | Email | Password |
|-----------|-------|----------|
| PlatformAdmin | `platform@healthmanager.local` | `ChangeMe123!` |
| Clinic Admin | `admin@clinicaaurora.com` | `ChangeMe123!` |

Seed IDs: `clinicId=11111111-1111-1111-1111-111111111111`, `platformAdminId=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa`, `clinicAdminId=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb`, `doctorId=cccccccc-cccc-cccc-cccc-cccccccccccc`, `patientId=dddddddd-dddd-dddd-dddd-dddddddddddd`, `appointmentId=eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee`, `receivableId=ffffffff-ffff-ffff-ffff-ffffffffffff`.

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
| `USE_INMEMORY_DATABASE` | No | `false` — tests set `true` + `INMEMORY_DATABASE_NAME` (per-class random) |
| `AWS_S3_BUCKET` | No | Without it, document storage falls back to local filesystem (`LOCAL_STORAGE_ROOT` or temp dir) |
| `SENTRY_DSN` | No | Optional, enables Sentry |
| `WHATSAPP_*` | No | WhatsApp webhook |

## API contract

`docs/openapi.json` — when changing request/response, update it and regenerate frontend client (`npm run generate:api` in frontend repo).

Manual testing via `src/HealthManager.Api/HealthManager.Api.http`.

## Infra

Terraform in `infra/` targets AWS (ECS Fargate + RDS + CloudFront + Lambda). CI/CD at `.github/workflows/backend-ci.yml` — deploy job only runs on master/main pushes (not PRs). Lambda builds `--self-contained --runtime linux-x64`.
