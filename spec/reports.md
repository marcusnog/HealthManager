# Relatórios da clínica

O [catálogo canônico](reports.json) especifica os 42 relatórios individualmente. Cada entrada contém título, finalidade, regra de cálculo e, quando necessária, fonte complementar. As mesmas definições aparecem na aba Relatórios.

## Plano de implementação

1. Identificar os registros disponíveis. A agenda guarda horário e situação atuais, os pagamentos guardam a parcela de receita da clínica, e o módulo de aplicações guarda entregas e saldo. Um saldo acumulado não permite reconstruir as datas nem os preços de vendas anteriores.
2. Consultar essas fontes diretamente. Registrar mudança de horário, cancelamento e venda de produto ou pacote no mesmo salvamento da operação que os originou.
3. Receber os dados complementares por clínica. O administrador pode carregar e corrigir as linhas pelo identificador de origem. Usar referências estáveis para o mesmo paciente, contrato e profissional. Nas vendas integradas, o paciente é identificado pelo GUID do cadastro.
4. Disponibilizar seleção de grupo, relatório e período, cálculo em BRL ou na unidade indicada, e a leitura dos resultados com a fórmula. Validar isolamento, limites de período, denominadores, custos ausentes e reimportação.

## Convenções de análise

O acesso inicial é do administrador da clínica porque os grupos reúnem informações operacionais e financeiras. O período é inclusivo no fuso da clínica. Consultas usam a situação atual dos registros na coorte selecionada. A ocupação histórica usa a grade semanal vigente. O saldo de aplicações é uma posição atual. Uma taxa sem denominador ou uma margem sem custo aparece como indefinida.

Consultas em grupo ocupam o tempo do médico uma vez. Cada participante conta na produtividade. A receita por hora usa a receita da clínica efetivamente recebida até o encerramento do período. O LTV acumula recebimentos identificados de toda a clínica. Venda de tratamento e recebimento são fatos distintos.

## Registro na ficha do paciente

Os vinte relatórios de reserva, fila, pontualidade, indicações, programas, cronograma, interrupções e insumos leem os registros de **Pacientes → Acompanhamento**. O [plano e mapa de origens](patient-care.md) descreve os vínculos com as consultas, contas a receber, pagamentos e aplicações. O CSV continua disponível para o histórico legado.

## Fontes complementares

O CSV usa UTF-8, separador ponto e vírgula e cabeçalho conforme o modelo da tela. Cada linha representa um registro operacional. A coluna `referencia` identifica a linha na fonte. Repetir a referência em uma nova importação substitui os campos dessa linha. Referências entre pacientes e profissionais devem ser consistentes. Usar identificadores internos em vez de CPF. Valores monetários admitem duas casas decimais e datas usam AAAA-MM-DD.

| Fonte | Significado de data | Grão da linha |
|---|---|---|
| Pagamentos de reserva | Pagamento do sinal | Um pagamento efetivo vinculado à reserva |
| Lista de espera | Entrada na fila | Uma espera, com saída opcional |
| Chegada e início da consulta | Atendimento | Uma consulta, horários locais no mesmo dia |
| Indicações de tratamento | Indicação | Um tratamento indicado, com referência opcional à consulta e data de contratação |
| Contratos de acompanhamento | Venda do contrato | Um contrato, com início de vigência opcional. O início assume a data da venda quando omitido |
| Entregas de programas | Contratação do item | Um lote homogêneo de unidades com a mesma previsão e entrega. Receita e custo são totais do lote |
| Parcelas de programas | Contratação | Uma parcela ou fração com a mesma data de baixa |
| Vendas de tratamentos | Venda | Uma contratação, inclusive pacote com várias aplicações. Receita e custo são totais |
| Cronograma de aplicações | Dia previsto | Uma etapa prescrita, com data de execução opcional |
| Interrupções de tratamento | Abandono declarado | Uma interrupção de protocolo |
| Uso de insumos | Consumo | Um movimento de material identificado por aplicação e unidade |

O CRM passa a registrar as novas vendas e remarcações a partir da implantação. O histórico comercial anterior pode ser carregado pela fonte Vendas. A conferência das referências e da cobertura histórica cabe à clínica. Datas de pagamento de reserva e de indicação exigem registros identificados especificamente para essas finalidades.


A classificação da venda como injetável ou protocolo pode ser indicada na concessão de saldo ou na correção da fonte. As demais vendas individuais e de pacotes participam dos totais comerciais e de aplicações.

## Especificações individuais

### Agenda

| Relatório | Finalidade | Cálculo e recorte |
|---|---|---|
| Novos agendamentos | Quantificar pacientes que contrataram uma reserva de data. | Pacientes distintos com pagamento de reserva positivo no período e soma dos valores pagos. Cada linha identifica um pagamento de sinal. |
| Taxa de ocupação | Comparar horários ocupados e disponíveis. | Minutos ocupados dentro da disponibilidade semanal vigente divididos pelos minutos disponíveis. Sobreposições do mesmo médico contam uma vez. Exclui cancelamentos. |
| Taxa de no-show | Medir a frequência de faltas. | Faltas divididas pela soma de consultas concluídas e faltas, pela data agendada. |
| Taxa de cancelamento | Medir a proporção de consultas canceladas. | Consultas atualmente canceladas divididas pelas consultas agendadas para o período. |
| Taxa de remarcação | Medir quantas consultas tiveram o horário alterado. | Consultas remarcadas divididas pelas consultas do período com histórico rastreado desde a criação. Agendamentos anteriores à implantação ficam fora da base. |
| Taxa de confirmação | Acompanhar a confirmação dos agendamentos. | Consultas com confirmação registrada divididas pelas consultas não canceladas do período. |
| Produtividade médica | Dimensionar os atendimentos de cada profissional. | Número de consultas concluídas por médico, pela data agendada. Cada paciente de uma consulta em grupo conta como um atendimento. |
| Receita por hora médica | Medir a receita da clínica por hora de atendimento. | Receita reconhecida nos pagamentos das consultas concluídas do período até a data final, dividida pelas horas agendadas dessas consultas, por médico. Intervalos simultâneos contam uma vez. |
| Ociosidade financeira | Estimar a receita potencial dos horários vagos. | Horas disponíveis desocupadas multiplicadas pela receita observada por hora do mesmo médico no período. Sem referência de preço por hora, a estimativa fica indefinida. |
| Antecedência de agendamento | Medir o intervalo entre a marcação e a consulta. | Média em dias entre a criação e o início agendado das consultas do período com intervalo não negativo. |
| Demanda reprimida | Dimensionar a fila por disponibilidade. | Pacientes distintos com entrada até o fim do período e sem saída nessa data. |
| Perfil de atendimento | Distinguir novos pacientes, retornos e acompanhamentos. | A primeira consulta concluída de cada paciente conta como primeiro atendimento. As seguintes são agrupadas pelo tipo cadastrado pela clínica, como retorno ou acompanhamento. |
| Pontualidade | Medir atraso do atendimento e espera do paciente. | Médias em minutos entre horário agendado e início (atrasos negativos valem zero) e entre chegada e início, pela data do atendimento. |
| Recuperação de agenda | Identificar horários vagos preenchidos após cancelamento. | Horários integralmente liberados antes do início da consulta e preenchidos por outra reserva do mesmo médico que cobre o intervalo original. A nova reserva deve ter sido criada entre o cancelamento registrado e o início agendado. Intervalos idênticos contam uma vez. |
| Conversão pós-consulta | Medir quantos pacientes contrataram tratamentos indicados em consulta. | Pacientes distintos com indicação vinculada a consulta no período e contratação até o fim do período divididos pelos pacientes distintos indicados nessas consultas. |

### Programas de acompanhamento

| Relatório | Finalidade | Cálculo e recorte |
|---|---|---|
| Carteira ativa por médico | Dimensionar a base acompanhada. | Pacientes distintos por médico com contrato vigente na data final, excluídos abandonos até essa data. |
| Novos programas vendidos | Medir o crescimento das contratações. | Contagem de contratos vendidos no período, por modalidade, pela data da venda. |
| Programas próximos ao vencimento | Antecipar renovações. | Contratos ativos na data final que vencem nos 30 dias seguintes, inclusive na data final, sem renovação registrada até essa data. Lista contrato, paciente, médico e dias até o vencimento. |
| Taxa de renovação | Medir retenção ao vencer o contrato. | Contratos com vencimento no período e renovação confirmada até a data final divididos pelos contratos vencidos no período. |
| Taxa de abandono | Identificar problemas de continuidade. | Contratos abandonados no período divididos pelos contratos vigentes em algum dia do período, incluindo o dia do abandono. |
| Serviços contratados x realizados | Controlar entregas aos pacientes. | Unidades previstas nos itens contratados no período e unidades desses itens entregues até a data final, por serviço. Entregas parciais são registradas em linhas separadas. |
| Consultas pendentes | Garantir o cumprimento das consultas contratadas. | Unidades de consulta contratadas até a data final com entrega em aberto nessa data, por programa. |
| Avaliações físicas pendentes | Monitorar a entrega das avaliações físicas. | Unidades de avaliação física contratadas até a data final com entrega em aberto nessa data, por programa. |
| Receita contratada futura | Prever ingressos financeiros já contratados. | Valor de parcelas com contratação até a data final, vencimento posterior e baixa ainda em aberto na data final. Parcelas parcialmente pagas são desdobradas por valor. |
| Obrigações futuras de atendimento | Planejar a capacidade operacional. | Unidades contratadas até a data final, com atendimento previsto após essa data e entrega em aberto, por serviço. |
| Custo de execução por programa | Medir o custo direto dos serviços entregues. | Soma do custo total efetivo das entregas realizadas no período, por programa. Custo ausente torna o total do grupo indefinido. |
| Margem por modalidade | Avaliar a sustentabilidade de cada modalidade. | Receita líquida das entregas do período menos seu custo direto por modalidade. Resultado em reais e percentual da receita líquida. Dados incompletos deixam a margem indefinida. |
| LTV por paciente | Medir o valor financeiro acumulado do relacionamento. | Soma histórica da receita reconhecida da clínica nos pagamentos identificados até a data final, por paciente. Recebimentos avulsos sem paciente ficam fora. LTV realizado, em reais. |

### Injetáveis e protocolos

| Relatório | Finalidade | Cálculo e recorte |
|---|---|---|
| Vendas por injetável | Analisar quantidade e faturamento. | Número de vendas classificadas como injetável e valor líquido contratado por produto, pela data da venda. |
| Vendas por protocolo | Avaliar o desempenho de cada tratamento. | Número de vendas classificadas como protocolo e valor líquido contratado por protocolo, pela data da venda. |
| Vendas por médico prescritor | Identificar a origem das indicações. | Quantidade e valor líquido das vendas no período por prescritor. Vendas sem atribuição aparecem em grupo próprio. |
| Vendas por colaborador | Medir o desempenho comercial. | Quantidade e valor líquido das vendas no período por responsável comercial, incluindo vendas sem atribuição. |
| Conversão de prescrições | Comparar tratamentos indicados e contratados. | Indicações emitidas no período com contratação até a data final divididas pelo total de indicações emitidas no período. Cada linha é um tratamento indicado. |
| Margem por injetável | Avaliar receita líquida após custos variáveis. | Receita líquida das vendas classificadas como injetável do período menos custos variáveis atribuídos, por produto. Custos ausentes deixam a margem indefinida. |
| Margem por protocolo | Medir a rentabilidade do tratamento completo. | Receita líquida das vendas classificadas como protocolo no período menos custos variáveis totais atribuídos ao tratamento, por protocolo. Custos ausentes deixam a margem indefinida. |
| Aplicações vendidas | Contar as aplicações contratadas. | Soma das unidades de aplicação incluídas nas vendas do período, por produto individual ou pacote. |
| Aplicações realizadas | Quantificar serviços efetivamente executados. | Soma das quantidades registradas por produto, pela data da aplicação. |
| Aplicações pendentes | Dimensionar o saldo de obrigações com pacientes. | Saldo atual por produto, somando unidades adquiridas menos utilizadas. É uma posição atual independente do período consultado. |
| Adesão ao tratamento | Medir o cumprimento do cronograma. | Etapas previstas no período executadas até o dia previsto divididas pelas etapas previstas no período. |
| Recompra | Identificar pacientes que voltaram a contratar. | Pacientes distintos com venda no período e pelo menos duas vendas até a data final. Exibe as compras do paciente no período e o total acumulado até a data final. |
| Abandono de tratamento | Identificar interrupções de protocolo. | Pacientes distintos com interrupção explícita no período por protocolo, pela data do abandono. Lista o paciente e a quantidade de interrupções. |
| Consumo de insumos | Controlar o consumo real por aplicação. | Quantidade efetivamente consumida por aplicação, insumo e unidade de medida, pela data de uso. Unidades diferentes ficam em linhas separadas. |
