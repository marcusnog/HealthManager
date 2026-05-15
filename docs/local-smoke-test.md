# Local Smoke Test

Use este roteiro para validar a API manualmente com o seed local.

## Pre-requisitos

- Docker Desktop ativo, ou PostgreSQL local acessivel
- API iniciada por `docker compose up --build` ou `dotnet run --project src/HealthManager.Api`
- Banco vazio ou sem conflito com o seed de desenvolvimento

## Credenciais seed

- `platform@healthmanager.local`
- `admin@clinicaaurora.com`
- senha para ambos: `ChangeMe123!`

## Ordem sugerida

1. Rode `GET /health`
2. Rode login de `PlatformAdmin`
3. Rode login de `Clinic Admin`
4. Rode `GET /dashboard/summary`
5. Rode `GET /patients`, `GET /doctors`, `GET /appointments`
6. Rode `POST /payments` com o `seedReceivableId`
7. Rode `POST /whatsapp/webhook` com `CONFIRMAR`
8. Rode `POST /internal/clinics` para validar provisionamento interno

## Arquivo pronto

Use [HealthManager.Api.http](/abs/c:/Users/NITRO%20V/Documents/HealthManager/src/HealthManager.Api/HealthManager.Api.http) para executar esse fluxo sem montar requests manualmente.

## IDs seed

- `clinicId`: `11111111-1111-1111-1111-111111111111`
- `doctorId`: `cccccccc-cccc-cccc-cccc-cccccccccccc`
- `patientId`: `dddddddd-dddd-dddd-dddd-dddddddddddd`
- `appointmentId`: `eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee`
- `receivableId`: `ffffffff-ffff-ffff-ffff-ffffffffffff`

## Observacoes

- O `PlatformAdmin` nao possui `clinic_id` e deve ser usado apenas para rotas internas.
- O `Clinic Admin` recebe `clinic_id` no token e pode acessar os modulos da clinica.
- O webhook atual interpreta mensagens contendo `CONFIRM` ou `CANCEL` para atualizar a consulta.
