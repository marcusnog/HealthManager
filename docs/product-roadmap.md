# HealthManager — roadmap de módulos

> Nota compatível com Obsidian. O MCP do Obsidian não estava disponível na sessão de criação.

## Ordem de implementação

### Entrega de 12/10/2026 — Atendimento da recepção via WhatsApp

Implementação local pronta para validação, baseada nas referências do Gestor360 no Obsidian. Inclui webhook Meta autenticado e isolado, deduplicação, estados de entrega, envio com tentativa persistida, paginação e rascunhos por conversa. Escopo confirmado: atendimento humano e Meta primeiro; IA fora desta etapa.

Specs em `spec/atendimento.yaml`; execução, cards Trello e evidências em [plano de atendimento](plans/atendimento-meta-2026-10-12.md). Homologação Meta e publicação permanecem pendentes.

### 1. Configurações gerais do tenant

- Ler e editar a clínica autenticada.
- Campos: nome, fuso, expediente, CNPJ, e-mail, telefone e endereço.
- Somente `Admin` altera; `Admin`, `Secretary` e `Doctor` podem consultar.
- Reutiliza `Clinic`; não cria tabela de chave/valor.

**Pronto quando:** API, OpenAPI, tela e teste de isolamento entre clínicas estiverem passando.

### 2. Atendimento clínico

- Um atendimento por consulta.
- Rascunho editável durante a consulta.
- Conteúdo inicial: queixa principal, história, exame, avaliação e plano.
- Finalização torna o registro imutável; correção posterior exige adendo auditável.
- Acesso de escrita para médico; equipe administrativa não acessa conteúdo clínico.

**Pronto quando:** médico iniciar consulta, salvar rascunho, finalizar e consultar histórico do paciente.

### 3. Hub de pagamentos

- Criar uma intenção de pagamento ligada a um recebível.
- Estados internos independentes de fornecedor.
- Idempotência na criação e no webhook.
- Adaptador de gateway somente quando um fornecedor for escolhido.
- Pagamento confirmado alimenta o fluxo financeiro existente.

**Pronto quando:** o hub operar em modo manual/fake e o primeiro gateway puder ser adicionado sem alterar agenda ou recebíveis.

## Decisões adiadas

- Prescrição, assinatura digital, modelos de prontuário e anexos clínicos: após validar Atendimento.
- Parcelamento, split, antecipação e recorrência: após escolher gateway.
- Configurações arbitrárias em JSON: somente quando surgir uma configuração que não pertença a uma entidade existente.
