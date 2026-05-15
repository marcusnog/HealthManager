# HealthManager API

Monolito modular para o MVP do CRM Medico SaaS.

## Estrutura

- `src/HealthManager.Api`: API REST, auth, Swagger e middlewares
- `src/HealthManager.Application`: casos de uso, DTOs, validacoes e regras
- `src/HealthManager.Domain`: entidades, enums e contratos centrais
- `src/HealthManager.Infrastructure`: EF Core, autenticacao, storage e integracoes
- `src/HealthManager.Worker`: processamento de outbox, lembretes e notificacoes
- `tests/HealthManager.Tests`: testes unitarios e de integracao
- `docs/openapi.json`: contrato OpenAPI publicado para o frontend

## Requisitos locais

- .NET 9 SDK
- PostgreSQL
- Supabase Storage
- `SUPABASE_URL`, `SUPABASE_KEY` e `SUPABASE_BUCKET` para upload binario real de documentos

## Ambientes

Use os arquivos:

- `.env.development`
- `.env.staging`
- `.env.production`

## Observacao

O ambiente atual nao possui `dotnet` instalado no PATH, mas o projeto foi validado com o executavel instalado em `C:\Program Files\dotnet\dotnet.exe`.

## Fluxo de desenvolvimento

1. Rode `dotnet restore HealthManager.sln`
2. Rode `dotnet build HealthManager.sln`
3. Rode `dotnet test HealthManager.sln`
4. Publique a API com `Dockerfile.api` e o worker com `Dockerfile.worker`
5. Gere o client web com `npm run generate:api` no repositorio frontend

## Banco e seed local

- A primeira migration do EF Core foi gerada em `src/HealthManager.Infrastructure/Persistence/Migrations`
- API e worker aplicam migrations automaticamente ao iniciar
- O seed local cria:
  - `PlatformAdmin`: `platform@healthmanager.local`
  - `Clinic Admin`: `admin@clinicaaurora.com`
  - senha inicial para ambos: `ChangeMe123!`
  - clinica demo, medico demo, paciente demo, consulta demo e conta a receber demo

## Subir com Docker Compose

1. Rode `docker compose up --build`
2. API: `http://localhost:8080`
3. Health check: `http://localhost:8080/health`
4. Swagger: `http://localhost:8080/swagger`

## Ferramentas locais

- O manifesto `.config/dotnet-tools.json` inclui `dotnet-ef` e `reportgenerator`
- Para criar novas migrations:
  `dotnet tool run dotnet-ef migrations add <Nome> --project src/HealthManager.Infrastructure --startup-project src/HealthManager.Api --context HealthManager.Infrastructure.Persistence.AppDbContext --output-dir Persistence/Migrations`
- Para gerar cobertura local:
  `dotnet test HealthManager.sln --collect:"XPlat Code Coverage" --results-directory ./TestResults`
  `dotnet tool run reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"CoverageReport" -reporttypes:"Html;Cobertura;MarkdownSummaryGithub"`

## Teste manual rapido

- Use `src/HealthManager.Api/HealthManager.Api.http` para executar login, dashboard, pacientes, agenda, financeiro, webhook e provisionamento interno
- O endpoint `POST /patients/{id}/documents/upload` envia o arquivo binario para o Supabase Storage quando `SUPABASE_URL` e `SUPABASE_KEY` estiverem configurados; sem essas variaveis o ambiente local continua registrando apenas metadata
- O roteiro resumido esta em `docs/local-smoke-test.md`
- A documentacao de cobertura esta em `docs/test-coverage.md`
