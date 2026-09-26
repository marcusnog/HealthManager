# #29 — Atalho para cadastrar paciente

## Resultado esperado

Durante um novo agendamento, a recepção cadastra um paciente sem perder os dados já preenchidos e retorna com o paciente selecionado.

## Dependências

- Nenhuma alteração de API ou banco.
- Reutilizar `POST /patients` e a invalidação da query de pacientes.

## Implementação

1. Frontend: separar o conteúdo do formulário de `src/modules/patients/patient-list.tsx` em um componente reutilizável, mantendo schema Zod e mutation no mesmo fluxo.
2. Frontend: adicionar `Cadastrar paciente` ao campo de paciente em `src/modules/scheduling/appointment-board.tsx`.
3. Frontend: abrir o formulário sem desmontar o formulário de agendamento.
4. Frontend: no sucesso, invalidar pacientes, selecionar o ID retornado e restaurar o agendamento; no cancelamento, apenas fechar o cadastro.

## Verificação

- Unitário: abrir/cancelar preserva os valores do agendamento.
- Unitário: salvar seleciona o paciente criado e permite concluir o agendamento.
- Regressão: cadastro pela tela Pacientes continua usando o mesmo formulário.

## Fora do escopo

- Cadastro simplificado com menos campos.
- Novo endpoint de paciente.
