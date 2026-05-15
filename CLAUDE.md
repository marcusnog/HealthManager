# HealthManager Handoff for Claude

Este arquivo resume o estado atual do trabalho para que outro agente consiga continuar sem perder contexto.

## Repositorios

- Backend atual: `C:\Users\NITRO V\Documents\HealthManager`
- Frontend separado: `C:\Users\NITRO V\Documents\healthmanager-web`

O produto foi construido como `monolito modular` no backend e `Next.js App Router` no frontend, mantendo dois repositorios desde o inicio.

## Direcao arquitetural

- Backend: `.NET 9`, `EF Core`, `JWT + refresh token`, `RBAC`, `tenant por clinic_id`, `soft delete`, `audit logs`, `outbox_events`, worker interno.
- Frontend: `Next.js + TypeScript + Tailwind + React Query + React Hook Form + Zod`.
- Contrato: `OpenAPI` publicado pelo backend em `docs/openapi.json`, com client gerado no frontend em `src/generated`.
- Infra alvo: `Railway` para API/worker, `Vercel` para web, `Supabase` para PostgreSQL e Storage.
- Regras do produto: Brasil-first, `pt-BR`, telefone/CPF BR, moeda BRL, agenda por fuso da clinica, multi-tenant desde o inicio.

## Estrutura importante do backend

- `src/HealthManager.Api`
- `src/HealthManager.Application`
- `src/HealthManager.Domain`
- `src/HealthManager.Infrastructure`
- `src/HealthManager.Worker`
- `tests/HealthManager.Tests`
- `docs/openapi.json`
- `src/HealthManager.Api/HealthManager.Api.http`

## Estrutura importante do frontend

- `src/app`
- `src/components/crm-workspace.tsx`
- `src/modules/auth`
- `src/modules/dashboard`
- `src/modules/patients`
- `src/modules/doctors`
- `src/modules/scheduling`
- `src/modules/financial`
- `src/generated`
- `tests/e2e-real/crm-api-integration.spec.ts`

## O que ja esta implementado

### Backend

- Auth com `login`, `refresh`, `logout`.
- Roles `PlatformAdmin`, `Admin`, `Secretary`, `Doctor`.
- Tenant isolation por `clinic_id`.
- CRUD principal de pacientes e medicos.
- Agenda com criacao, validacao de conflito, confirmacao e cancelamento.
- Financeiro com `receivables` + `payments`, incluindo pagamento parcial.
- Dashboard resumo.
- Webhook WhatsApp.
- Documentos de paciente com listagem, upload multipart, download autenticado e soft delete.
- Outbox + worker para eventos e notificacoes.
- Swagger, `ProblemDetails`, logs estruturados, Sentry condicional.
- Seed local com clinica demo e usuarios demo.

### Frontend

- Login real com sessao JWT.
- Dashboard com metricas reais.
- Pacientes com:
  - criacao
  - edicao
  - busca
  - paginacao
  - documentos: upload, download, exclusao
- Medicos com cadastro e edicao.
- Agenda com:
  - criacao
  - confirmacao
  - cancelamento
  - navegacao por data
- Financeiro com registro de pagamento parcial e refresh da UI.
- Fluxos E2E reais contra a API local.

## Seed local

Credenciais atuais:

- `platform@healthmanager.local` / `ChangeMe123!`
- `admin@clinicaaurora.com` / `ChangeMe123!`

O seed cria:

- clinica demo
- medico demo
- paciente demo
- consulta demo
- conta a receber demo

## Estado validado em 2026-05-07

### Backend

- `dotnet test HealthManager.sln`: 19 testes aprovados

### Frontend

- `npm run test`: 16 testes aprovados
- `npm run build`: OK
- `npm run test:e2e:real`: 8 testes aprovados

Os cenarios E2E reais atuais cobrem:

1. login com o usuario seed
2. criacao de paciente
3. criacao de consulta
4. busca e paginacao de pacientes
5. navegacao da agenda por data
6. edicao de paciente e medico
7. confirmacao e cancelamento de consulta
8. pagamento parcial
9. upload, download e remocao de documento

## Arquivos-chave para continuar

### Backend

- `src/HealthManager.Api/Program.cs`
- `src/HealthManager.Application/Abstractions.cs`
- `src/HealthManager.Application/Contracts.cs`
- `src/HealthManager.Application/Services.cs`
- `src/HealthManager.Infrastructure/Persistence/AppDbContext.cs`
- `src/HealthManager.Infrastructure/Persistence/AppDbInitializer.cs`
- `src/HealthManager.Api/Controllers/PatientsController.cs`
- `src/HealthManager.Api/Controllers/AppointmentsController.cs`
- `src/HealthManager.Api/Controllers/FinancialController.cs`
- `tests/HealthManager.Tests/Integration`

### Frontend

- `src/components/crm-workspace.tsx`
- `src/modules/patients/patient-list.tsx`
- `src/modules/doctors/doctor-roster.tsx`
- `src/modules/scheduling/appointment-board.tsx`
- `src/modules/financial/financial-overview.tsx`
- `src/services/api.ts`
- `tests/e2e-real/crm-api-integration.spec.ts`

## Regras de continuidade

- Sempre preservar `monolito modular`; nao quebrar em microsservicos agora.
- Se mudar request/response do backend, atualizar `docs/openapi.json` e regenerar o client do frontend.
- Continuar usando o client gerado; evitar `fetch` ad-hoc.
- Manter `clinic_id` em qualquer entidade da clinica.
- Continuar usando `receivables + payments`, nao voltar para um unico status simplificado.
- Guardar datas em UTC e exibir conforme o contexto da clinica.
- Tratar estado de `loading`, `empty`, `error` e `role` no frontend.
- Manter testes junto do fluxo alterado.

## Como rodar

### Backend

```powershell
dotnet restore HealthManager.sln
dotnet build HealthManager.sln
dotnet test HealthManager.sln
docker compose up --build
```

API local:

- `http://localhost:8080`
- Swagger: `http://localhost:8080/swagger`
- Health: `http://localhost:8080/health`

### Frontend

```powershell
cd C:\Users\NITRO V\Documents\healthmanager-web
npm install
npm run generate:api
npm run lint
npm run test
npm run test:e2e
npm run test:e2e:real
npm run build
```

Observacao:

- `npm run test:e2e:real` sobe o frontend e a API em memoria para validar o fluxo real com o seed local.

## Variaveis importantes

Backend:

- `DATABASE_URL`
- `JWT_SECRET`
- `WHATSAPP_TOKEN`
- `WHATSAPP_PHONE_ID`
- `SUPABASE_URL`
- `SUPABASE_KEY`
- `SUPABASE_BUCKET`

Sem as variaveis do Supabase, o ambiente local continua aceitando o fluxo de documentos, mas pode operar de forma reduzida para storage binario real.

## Proximos passos recomendados

1. Paginar e filtrar a agenda e o financeiro, assim como ja foi feito em pacientes.
2. Adicionar exclusao logica ou inativacao de pacientes e medicos na UI.
3. Endurecer o fluxo de indisponibilidade do medico e expediente da clinica na UX.
4. Cobrir webhook WhatsApp e worker com mais testes E2E ou integracao dirigida.
5. Preparar deploy real com secrets e pipelines finais para Railway/Vercel/Supabase.

## Riscos e observacoes

- Existem muitas mudancas ainda nao commitadas nos dois repositorios; nao assumir historico limpo.
- O frontend tem artefatos locais de teste como `playwright-report-real` e `test-results`; evitar confundir isso com codigo de produto.
- O arquivo `healthmanager-web/CLAUDE.md` ainda e um placeholder apontando para `AGENTS.md`; este arquivo aqui e o handoff mais completo neste momento.
