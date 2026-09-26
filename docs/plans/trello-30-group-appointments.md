# #30 — Mais de uma pessoa no mesmo horário

## Resultado esperado

Criar, em uma única operação, consultas independentes para vários pacientes atendidos juntos pelo mesmo profissional.

## Dependências e ordem

1. Backend e migração.
2. OpenAPI e cliente gerado.
3. Interface.

## Implementação do backend

1. Adicionar `AppointmentGroupId` nullable em `Appointment`, configuração EF e `AppointmentResponse`.
2. Criar `CreateGroupAppointmentRequest` com `patientIds` e os campos compartilhados definidos na spec.
3. Extrair de `AppointmentService.CreateAsync` somente a montagem comum necessária; manter uma transação para o lote.
4. Validar pacientes distintos, mesmo tenant, horário comercial e conflito externo ao grupo antes de persistir qualquer consulta.
5. Criar um `appointmentGroupId` e persistir uma consulta/recebível/outbox por paciente.
6. Expor `POST /appointments/group` em `AppointmentsController`.
7. Criar migração, atualizar `docs/openapi.json` e regenerar o cliente frontend.

## Implementação do frontend

1. Adicionar alternância `Atendimento individual/em grupo` ao formulário atual.
2. No modo grupo, usar seleção múltipla de pacientes e impedir duplicados.
3. Enviar o novo contrato sem alterar o fluxo individual.
4. Identificar consultas do mesmo grupo e permitir sobreposição visual no dia/semana.

## Verificação

- Serviço: cria N consultas e N recebíveis com o mesmo grupo.
- Serviço: erro em um paciente não persiste nenhum item.
- Integração: rejeita outro tenant, duplicados e conflito externo; aceita membros do mesmo grupo.
- EF: `has-pending-model-changes` passa.
- Frontend: individual permanece inalterado; grupo renderiza todos os participantes.

## Decisão fechada

Cada participante possui status, cobrança e prontuário próprios; não será criada uma entidade clínica coletiva.
