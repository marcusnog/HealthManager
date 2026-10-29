# Preparação do backup MedX

`medx_prepare.py` valida o backup e prepara candidatos à importação. Não faz chamadas HTTP nem grava no banco do HealthManager.

Requer Python 3.11+ e 7-Zip instalado. Execute fora de ambientes que publiquem os arquivos gerados:

```powershell
python scripts/medx_prepare.py --source 'D:/Downloads/medx-bkp' --output 'D:/Downloads/medx-bkp/staging-novo' --subscription 16925
python -m unittest discover -s scripts -p test_medx_prepare.py
```

A senha é solicitada sem eco, ou lida de `MEDX_BACKUP_PASSWORD`. O diretório de saída deve ser novo e ficar fora dos dois repositórios. Ele contém dados pessoais e clínicos em texto aberto: mantenha-o privado, sem versionamento ou publicação.

- `manifest.json`: origem, hashes, tabelas e estado da preparação.
- `summary.json`: contagens e pendências, sem conteúdo clínico.
- `tables/`: tabelas originais, sem alterações.
- `patients.ndjson`: candidatos, chaves de origem e motivos de retenção.
- `history.ndjson`: referências às linhas originais, datas e autores.
- `files.ndjson` e `document-links.ndjson`: inventário e vínculos explícitos de anexos.

`source_validated` indica apenas validação da origem. Antes de importar, confirme a instância e o tenant, faça backup do destino, compare identidades existentes (inclusive excluídas) e mantenha um vínculo persistente de origem/destino para evitar duplicações. Não repita uma criação cujo resultado ficou incerto sem reconciliá-la.

Históricos permanecem pendentes de mapeamento de profissionais e atendimentos. O texto original é preservado; a identificação de formato não converte RTF/HTML. Agenda, financeiro e credenciais não são importados por este script. As regras completas estão em `spec/medx-import.yaml`.

## Primeira carga de pacientes pela API

`medx_import_patients.py` cria apenas candidatos com CPF válido. Exige URL HTTPS, ID e nome exatos da clínica, usuário Admin e limite explícito de criações. A senha é solicitada ou lida de `HEALTHMANAGER_IMPORT_PASSWORD`.

```powershell
python scripts/medx_import_patients.py --stage '<diretório privado preparado>' --base-url 'https://<host>/backend' --clinic-id '<guid>' --clinic-name '<nome exato>' --email '<admin da clínica>' --limit 10
python -m unittest discover -s scripts -p 'test_medx*.py'
```

Antes de gravar, salva e verifica uma cópia dos pacientes ativos. Essa cópia não substitui um backup completo do banco. O índice único de clínica/CPF também protege contra CPFs de registros excluídos; conflitos ficam retidos. Sem CPF, a criação aguarda consulta das identidades excluídas. Nomes iguais também são retidos, sem mesclagem automática.

Cada criação recebe uma referência MedX nas observações, preservando as observações originais, e um registro persistente antes/depois da chamada. A retomada confere o vínculo antes de criar. Timeout ou resposta incerta interrompe a carga: não remova o registro de intenção nem repita manualmente o POST. Investigue o destino primeiro. Um arquivo `running.lock` após encerramento inesperado exige verificar que o processo terminou antes de removê-lo.

O importador verifica os campos importados ao final. Não importa consultas, financeiro, usuários nem anexos. Não altera os pacientes preexistentes.

## Cadastros e anexos

Após obter um backup verificado do banco e as identidades excluídas do tenant, `--deleted-identities <arquivo privado>` permite ao importador de pacientes conferir também os nomes excluídos e liberar candidatos sem CPF. O recibo do servidor precisa ser recente e corresponder à clínica.

`medx_catalog.py` prepara profissionais, procedimentos como itens de aplicação e modelos de exames. Usa os mesmos argumentos de destino do importador de pacientes; `--apply` executa o plano. Não cria contas de acesso, não altera os cadastros anteriores e não converte referências clínicas em limites automáticos. O único percentual financeiro atribuído a novos médicos é o padrão do sistema (100% clínica), que deve ser revisto antes de utilizar o profissional em cobranças. Os modelos indicam que as referências importadas precisam de revisão clínica.

`medx_documents.py` recebe os mesmos argumentos de destino, `--stage`, `--source` (ZIPs), `--limit` e `--workers` (1 a 8). Copia os PDFs/JPEGs/PNGs originais para os pacientes comprovados pelo journal e pela API. Exige backup recente e armazenamento persistente previamente conferido. Verifica hash do ZIP, CRC/tamanho do membro, limite multipart e hash do download após cada envio. A senha do backup vem do prompt ou de `MEDX_BACKUP_PASSWORD`.

Os arquivos recebem prefixo MedX; data/autoria impressas no original permanecem intactas. A data de inclusão no HealthManager é a da importação. Isso não cria um prontuário nativo, não atribui autoria a um médico do destino e não produz nova assinatura.

Arquivos referenciados por mais de um paciente, formatos não suportados e resultados incertos ficam pendentes. O journal de anexos permite retomar os lotes sem reenviar os documentos já verificados. Nunca apague uma intenção pendente para forçar repetição.

Com `--verify-on-server`, o envio fica marcado como `uploaded` até a conferência independente dos bytes persistidos. Execute `medx_verify_server.py` com o relatório criptografado do servidor para promover apenas arquivos com identidade, tamanho e SHA-256 conferidos. Não execute essa reconciliação durante um lote ativo.

`medx_catalog.py --include-document-templates` inclui modelos explicitamente compartilhados e de HTML simples. Mantém a hierarquia de pastas e a proveniência; retém modelos privados, ricos, com placeholders ou acima dos limites. Aplique somente após disponibilizar o limite de catálogo de 1000 itens. A formatação convertida deve ser revisada antes do uso clínico.

Para pausar um lote, crie `document-import/pause.request` no staging. O importador termina as requisições em curso e para antes do próximo arquivo; espere o processo encerrar e confira que não há intenções pendentes. Remova esse arquivo somente ao retomar. A verificação de relatórios criptografados também requer o pacote Python `cryptography`.

Quando o endereço público limitar a carga, `HEALTHMANAGER_SSH_TUNNEL_PORT` permite usar um encaminhamento SSH previamente verificado, ligado somente a `127.0.0.1`. Fixe a chave pública do VPS em `known_hosts`, restrinja a chave temporária ao serviço web/API confirmado e proíba comandos. A identidade HTTPS canônica e a validação da clínica permanecem iguais nos journals. Não desative verificação de certificados; remova o encaminhamento e revogue a chave temporária ao terminar.

`--include-appointment-types` importa os nomes de tipos de agendamento após consultar os excluídos. A duração original fica no mapeamento privado, pois o tipo no destino não possui esse campo. Não cria consultas nem interpreta códigos de status.
