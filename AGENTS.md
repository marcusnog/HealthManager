# HealthManager (backend)

.NET 9 modular monolith — CRM médico multi-tenant.

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
docker compose up --build        # API at :8080, Swagger at /swagger
```

Tools in `.config/dotnet-tools.json`: `dotnet-ef`, `reportgenerator`.

```powershell
dotnet tool run dotnet-ef migrations add <Nome> --project src/HealthManager.Infrastructure --startup-project src/HealthManager.Api --output-dir Persistence/Migrations
dotnet tool run reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"CoverageReport" -reporttypes:"Html;Cobertura;MarkdownSummaryGithub"
```

CI builds with `--configuration Release`. Tests use `Microsoft.AspNetCore.Mvc.Testing` + EF Core InMemory + xUnit + FluentAssertions.

## Packages

- **Api**: Api, Application, Infrastructure (no direct Domain ref — via Application)
- **Application**: Application, Domain
- **Infrastructure**: Infrastructure, Application, Domain
- **Tests**: Api + Application + Domain + Infrastructure (all project refs)

Program.cs bypasses HTTPS in `Testing` env (check `!app.Environment.IsEnvironment("Testing")`).

## Env vars required at runtime

`DATABASE_URL`, `JWT_SECRET`, `WHATSAPP_TOKEN`, `WHATSAPP_PHONE_ID`, `SUPABASE_URL`, `SUPABASE_KEY`, `SUPABASE_BUCKET`.

Without Supabase vars, document upload still works (metadata only, no binary storage).

## Seed credentials

- `platform@healthmanager.local` / `ChangeMe123!`
- `admin@clinicaaurora.com` / `ChangeMe123!`

Seed IDs for manual testing (in `docs/local-smoke-test.md` and `src/HealthManager.Api/HealthManager.Api.http`):
`clinicId=11111111-1111-1111-1111-111111111111`, `doctorId=cccccccc-cccc-cccc-cccc-cccccccccccc`, `patientId=dddddddd-dddd-dddd-dddd-dddddddddddd`, `appointmentId=eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee`, `receivableId=ffffffff-ffff-ffff-ffff-ffffffffffff`.

## Key architecture

- Modular monolith, not microservices — keep it that way.
- Tenant isolation via `clinic_id` on every entity.
- Finance uses `receivables + payments` (partial payment supported), not a single status.
- Dates in UTC; display in clinic timezone.
- Outbox + Worker for async events/notifications.
- Lambda function in `.sln` (`HealthManager.Lambda`) — used for outbox processing in AWS deploy.
- `docs/openapi.json` is the API contract. When changing request/response, update it and regenerate frontend client (`npm run generate:api` in the frontend repo).

## Infra deploy

Terraform in `infra/` targets AWS (ECS Fargate + RDS + CloudFront + Lambda). CI/CD pushes ECR images and updates ECS service. GitHub Actions workflow at `.github/workflows/backend-ci.yml`.
