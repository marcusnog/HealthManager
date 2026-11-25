# Acompanhamento na ficha do paciente

O administrador registra os fatos abaixo em Pacientes → Acompanhamento. A secretaria pode registrar fila e horários. Os 20 indicadores usam esses registros juntamente com o histórico importado. As fórmulas, datas de corte e regras de base vazia continuam em reports.json. As oito análises da fonte vendas continuam no fluxo comercial existente.

| Fluxo | Reaproveitar do CRM | Registrar no atendimento | Relatórios |
|---|---|---|---|
| Reserva | Pagamento recebido, paciente, valor e data | Marcar o pagamento como sinal. Para um pagamento avulso, selecionar uma consulta da mesma ficha. Para um pagamento de consulta, preservar a consulta de origem. Cada pagamento só pode ser marcado uma vez. A conciliação preserva o título, o recebimento e os saldos | Novos agendamentos |
| Fila | Paciente | Entrada e saída | Demanda reprimida |
| Atendimento | Consulta e horário agendado | Chegada e início real | Pontualidade |
| Indicação | Paciente, consulta de origem e contrato de acompanhamento quando houver | Tratamento indicado, data da indicação. Um contrato vinculado fornece a data da conversão. Para outras modalidades, registrar a data de contratação confirmada | Conversão pós-consulta, Conversão de prescrições |
| Programa | Paciente e profissional | Modalidade, contratação, início, vencimento, renovação e abandono | Carteira ativa por médico, Novos programas vendidos, Programas próximos ao vencimento, Taxa de renovação, Taxa de abandono |
| Serviços do contrato | Contrato, modalidade e consulta quando vinculada | Serviço, categoria, unidades, previsão, receita líquida e custo do lote. Uma consulta vinculada conta como uma unidade entregue ao ser concluída na agenda. Para os demais lotes, registrar a data de entrega e dividir entregas parciais | Serviços contratados x realizados, Consultas pendentes, Avaliações físicas pendentes, Obrigações futuras de atendimento, Custo de execução por programa, Margem por modalidade |
| Parcelas | Título financeiro do paciente, vencimento, principal e pagamentos efetivos até o corte | Associar o título ao programa | Receita contratada futura |
| Etapas | Paciente e aplicação executada | Protocolo e dia previsto, vincular a aplicação ao executar | Adesão ao tratamento |
| Interrupção | Paciente | Protocolo e data do abandono declarado | Abandono de tratamento |
| Insumos | Aplicação e dia da execução | Material efetivamente consumido, unidade e quantidade | Consumo de insumos |

## Implementação e histórico

1. Vincular entidades da mesma clínica e paciente. Manter cadastro, edição com revisão e seleção por nome/data na ficha. Uma consulta tem uma aferição de horários, um pagamento tem uma marcação de sinal e um título pertence a um único contrato. Uma execução pode atender uma etapa.
2. Ler horários agendados, valores e datas já conhecidos. Considerar apenas pagamentos válidos para o sinal. Deduzir os recebimentos até o corte da previsão de parcelas. Registrar chegada antes do início e permitir completar o apontamento depois.
3. Aplicar as fórmulas existentes aos fatos nativos e preservar as linhas históricas legadas. Ao migrar um fato já importado, manter sua referência original e reconciliá-lo antes de lançá-lo novamente. A tela de relatórios indica o caminho na ficha. Ausência de horário real, aceite, custo ou consumo continua sem estimativa.
4. Validar o fluxo integral sem CSV, segregação por paciente/clínica, restrições de acesso, edição e datas de corte.
