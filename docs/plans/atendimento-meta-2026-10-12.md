# Atendimento humano e Meta — plano de implementação

Base: `spec/atendimento.yaml` e specs canônicas de entidades, regras, estados e endpoints. Escopo confirmado pelo usuário: atendimento humano e Meta primeiro; agente de IA fora desta etapa.

## Referência e diagnóstico

Notas consultadas no vault `C:/Users/Marcus Nogueira/Documents/my-obsidian-vault/obsidian`: `Projetos/Gestor360 - Estado atual.md`, `Projetos/gestor360-meta-migration.md`, `Bugs/gestor360-whatsapp-cross-company-leak.md`. Retratam 13/08/2026, não uma auditoria da produção hoje.

Reaproveitar configuração por tenant, webhook nativo Meta, assinatura, inbox e acompanhamento de entrega. Evitar os problemas documentados: fallback de tenant, contatos mesclados sem vínculo e contexto de outra empresa. Não copiar módulos de IXC/RAG, sessões de IA nem DDL manual: HealthManager mantém EF migrations e monólito modular.

Problemas confirmados no HealthManager: webhook anônimo aceita clínica/consulta do cliente; textos contendo CONFIRM/CANCEL modificam agenda sem regras financeiras; histórico inteiro materializado; envio consulta a última mensagem por telefone e pode retornar outro envio concorrente; UI mostra check para falhas, lista não faz polling, texto compartilhado entre conversas.

## Execução em ordem

1. **AT-01 — Ingestão Meta segura e isolada.** Extrair serviço; HMAC obrigatório do corpo bruto, challenge fechado sem configuração, envelope nativo e tenant exclusivamente pelo número Meta. Remover alteração de agenda por texto. Deduplicar e processar statuses monotônicos. Aceite: testes de assinatura, clínica divergente, replay e status fora de ordem.
2. **AT-02 — Persistência, envio e consultas.** Índices únicos e histórico normalizado por migração; preservar timestamps do provedor; Pending durável antes do envio; chave idempotente; retorno exato; janela de 24 horas para resposta livre. SQL paginado para lista/histórico. Aceite: isolamento, limites, repetição, incerteza e snapshot EF.
3. **AT-03 — Central de atendimento e contrato.** Atualizar OpenAPI/client; paginação, polling, mensagens antigas, rascunhos/erros por destinatário, estados legíveis e mensagens de recuperação. Aceite: testes de troca de conversa durante envio, falha e paginação; lint/build.
4. **AT-04 — Verificação e operação.** Testes backend e frontend, build, migration snapshot, revisão das mudanças e instruções de configuração/ativação. Mover cards para Validação somente com evidência local; não marcar Produção sem deploy.

## Decisões de arquitetura

- Serviços em arquivos próprios, usando interfaces existentes. Sem broker, microserviço ou mecanismo genérico de sessões.
- Chave de conversa é clínica + telefone; nomes são auxílio visual, nunca autorização de identidade.
- Envio síncrono com tentativa persistida. Timeout tem resultado desconhecido e não deve gerar reenvio automático. Uma fila durável de saída é evolução distinta, não promessa de exactly-once.
- Índices únicos garantem deduplicação entre processos. Conflito concorrente devolve 409 e reentrega é segura.
- Migração não apaga histórico: duplicatas legadas perdem apenas providerMessageId repetido, preservando linhas; número Meta duplicado exige correção antes de aplicar índice.
- Mídia recebida fica identificada como não suportada; upload/download, templates na central, distribuição entre atendentes e IA são evoluções fora desta entrega.

## Validação e operação

Configurar `WHATSAPP_APP_SECRET` e `WHATSAPP_VERIFY_TOKEN` no servidor; configurar número/token Meta por clínica; cadastrar callback HTTPS `/whatsapp/webhook`. Nunca enviar mensagens reais durante testes. Credenciais e webhook reais precisam de homologação após deploy.

Referência de protocolo: [Meta — webhook payload](https://www.postman.com/meta/whatsapp-business-platform/folder/tduohwq/webhook-payload-reference) e [Meta — verificação do webhook](https://whatsapp.github.io/WhatsApp-Nodejs-SDK/api-reference/webhooks/start/).

## Registro de execução

Specs concluídas antes das alterações de código. Cards e resultados serão registrados abaixo durante a execução.

| Etapa | Card |
|---|---|
| AT-01 | [Webhook Meta seguro e isolado](https://trello.com/c/U4y5tKHn) |
| AT-02 | [Envio confiável e histórico paginado](https://trello.com/c/2pVTXogn) |
| AT-03 | [Central humana e contrato frontend](https://trello.com/c/cI0ejxXP) |
| AT-04 | [Regressões e homologação](https://trello.com/c/azaTeJj9) |

Implementação local:

- `WhatsAppWebhookService`, `WhatsAppMessagingService` e `WhatsAppAtendimentoService` extraídos para arquivos próprios; interfaces e registro de DI existentes preservados.
- Webhook com assinatura obrigatória, limite de tamanho, tenant pelo número Meta e replay sem duplicação. Status com concorrência otimista e transições monotônicas. Palavras recebidas não alteram mais agenda/financeiro.
- Tentativa Pending persistida antes do HTTP, chave idempotente por clínica e resultado do registro exato. Erro de transporte/5xx mantém resultado desconhecido, sem retry automático.
- Migração `20261013010212_WhatsAppAtendimentoReliability` normaliza telefones, preserva linhas históricas, cria índices e colunas de timestamp/chave de envio. Dados legados sem timestamp Meta não abrem a janela de resposta: uma nova mensagem do paciente é necessária.
- Listagem e histórico paginados no SQL. Nome só aparece quando há um único paciente com o telefone; telefones compartilhados não vinculam identidade.
- OpenAPI e cliente gerado atualizados. UI com polling de 10s, navegação de páginas, mensagens anteriores, rascunhos/erros por conversa e estados legíveis. Timestamp do histórico reabre a janela mesmo se a conversa não estiver na página atual da lista.

### Ativação e compatibilidade

1. Antes do deploy, verificar números Meta duplicados entre configurações não excluídas. A migração bloqueia ambiguidades em vez de escolher uma clínica arbitrariamente.
2. Provisionar `WHATSAPP_APP_SECRET` e `WHATSAPP_VERIFY_TOKEN` no processo da API; sem segredo o webhook retorna 503. O segredo é do aplicativo Meta, não o access token do número.
3. Manter configuração de número/token por clínica habilitada e clínica ativa. Números desconhecidos/desabilitados são ignorados sem gravação; concluir o vínculo antes de ativar a assinatura de eventos.
4. Publicar backend primeiro, depois frontend: chamadas com paginação retornam `PagedResult`; chamadas sem page/pageSize preservam temporariamente o array cronológico das 50 mensagens mais recentes para o cliente anterior. Remover integrações que enviavam o DTO legado com `clinicId`/`appointmentId`; apenas envelope Meta assinado é aceito.
5. Configurar callback HTTPS `/whatsapp/webhook`, challenge e assinatura de eventos de mensagens no aplicativo Meta. Homologar recebimento, resposta e status com número de teste autorizado.
6. Timeout após envio pode ficar Pending sem providerMessageId; exigir conferência operacional antes de iniciar outra tentativa. A UI não apresenta isso como mensagem enviada.

### Limites preservados e próximos passos

Agente IA excluído por decisão do usuário. Mídia recebida aparece como marcador; upload/download, templates manuais na central, filas/distribuição por atendente e estado aberto/resolvido não fazem parte desta entrega. Status sem correspondência com mensagem persistida são ignorados; reconciliação de callbacks precoces e de resultados desconhecidos é evolução futura. A proteção em repouso das credenciais já armazenadas pela integração deve ser tratada com um keyring persistente antes de ampliar o uso; não foi introduzida criptografia com chaves descartáveis neste patch.

Avisos existentes: quatro avisos de nulabilidade no backend e sete de lint em outros módulos. O teste de navegador com sessão semeada em localStorage também registra divergência de hidratação no shell inicial; o fluxo de atendimento passa após a hidratação. Não houve deploy, alteração de credenciais reais nem envio a pacientes.

### Evidências de validação

- Suíte backend completa em Release: **130 aprovados**. Após os ajustes finais, seleção WhatsApp/Meta em Release: **25 aprovados**, incluindo PostgreSQL real e os testes adicionais de protocolo.
- PostgreSQL 17 temporário em `127.0.0.1:55439`: teste cria banco com nome aleatório e o exclui ao terminar. Validou upgrade da migração anterior com mensagens duplicadas/telefone DDD 55, preservação das linhas, índice único, busca por nome e paginação real. Servidor temporário desligado após o teste.
- `dotnet build HealthManager.sln --configuration Release --no-restore`: aprovado; API, Worker e Lambda compilados.
- `dotnet-ef migrations has-pending-model-changes`: sem alterações pendentes.
- Frontend: **63 testes aprovados**, build Next.js/TypeScript aprovado e lint sem erros. A execução inicial paralela teve timeout em um teste financeiro existente; repetição isolada e suíte final com um worker passaram.
- Playwright/Chrome: **2 testes aprovados**, 1280px e 390px, API simulada, envio com UUID e status Lida, retorno para lista no celular e ausência de overflow horizontal. Capturas revisadas em `artifacts/atendimento-2026-10-12/` no backend.
- As seis specs YAML modificadas e o JSON OpenAPI foram parseados; cliente regenerado. Diff sem erros de whitespace considerando CRLF do Windows.

Reprodução do teste relacional: definir `HEALTHMANAGER_TEST_POSTGRES` para um servidor PostgreSQL isolado com permissão de criar bancos e executar `dotnet test --filter FullyQualifiedName~WhatsAppPostgreSqlTests`. Sem essa variável, somente esse teste relacional é marcado como ignorado; o restante usa InMemory como antes. E2E local pode usar `PLAYWRIGHT_CHANNEL=chrome`; sem a variável utiliza Chromium configurado pelo projeto.

Entrega local pronta para validação funcional. Os quatro cards seguem para **Validação**, sem marcação de Produção.

### Review antes da publicação

- Corrigida incompatibilidade entre deploys independentes: resposta legada preservada para o cliente anterior, com teste de isolamento e equivalência da paginação.
- Corrigido repasse de WHATSAPP_APP_SECRET e WHATSAPP_VERIFY_TOKEN nos manifests Docker Compose. K3s precisa fornecer ambos ao processo por sua configuração de ambiente existente.
- Deploy pelo GitHub Actions; a identidade SSH local não autenticou, portanto credenciais Meta e duplicidades na base de produção não foram verificadas diretamente. A migração rejeita configurações ambíguas e o pipeline possui rollback de rollout.

- Review final: suíte Release completa com **138 aprovados, 1 ignorado** (PostgreSQL opcional, já validado separadamente), incluindo compatibilidade do cliente anterior; snapshot EF sem pendências.
