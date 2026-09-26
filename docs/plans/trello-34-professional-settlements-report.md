# #34 — Entendimento e relatório de repasses

## Resultado esperado

A tela explica o cálculo e permite conferir e filtrar os passivos de profissionais com os dados existentes.

## Dependências

- Primeiro confirmar os campos retornados por `GET /financial/professional-settlements`.
- Manter a permissão `FinanceSettlements` e a regra de que repasse não é despesa.

## Implementação do backend

1. Cobrir a resposta atual com teste integrado: profissional, totais, paciente, data, valor e atraso.
2. Não alterar o endpoint se esse contrato estiver completo.
3. Se faltar dado indispensável, adicioná-lo a `ProfessionalSettlementItemResponse`, specs e OpenAPI antes do frontend.

## Implementação do frontend

1. Adicionar explicação curta do cálculo no topo da aba `Fechamento e repasse`.
2. Exibir cards de acumulado, pago e pendente usando os totais retornados, sem recalcular regra financeira no cliente.
3. Manter tabela, filtros e seleção atuais; nomear claramente estados pendente/atrasado/repassado.
4. Manter o relatório em tela; não criar exportação sem solicitação explícita.

## Verificação

- Backend: valores e atraso corretos; acesso negado sem `FinanceSettlements`.
- Frontend: totais, filtros e seleção usam o mesmo conjunto de itens.
- Regressão: baixa selecionada continua enviando apenas `paymentIds` marcados.

## Fora do escopo

- PDF, agendamento ou envio automático do relatório.
- Tratar repasse como despesa.
