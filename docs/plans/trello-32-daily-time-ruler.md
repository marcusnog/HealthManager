# #32 — Régua de horários do dia

## Resultado esperado

A agenda diária representa o dia como escala temporal, incluindo espaços livres, duração das consultas e indicador do horário atual.

## Dependências

- Nenhuma alteração de backend.
- Reutilizar consultas, data selecionada e horário da clínica já carregados.

## Implementação

1. Definir o intervalo visível a partir do horário da clínica, com fallback atual da agenda.
2. Criar marcas regulares de horário por CSS; calcular em TypeScript apenas deslocamento e altura das consultas.
3. Posicionar consultas por `startAt` e `endAt`, preservando acesso ao cartão e aos atalhos.
4. Mostrar a linha de `agora` somente no dia corrente e atualizá-la com um único timer por minuto.
5. Em telas pequenas, manter rolagem vertical e uma lista textual acessível na mesma ordem cronológica.

## Verificação

- Unitário: posição nos limites de abertura/fechamento e diferentes durações.
- Unitário: linha atual aparece apenas hoje.
- Visual/E2E: horários vazios, consultas simultâneas e viewport móvel.

## Fora do escopo

- Biblioteca externa de calendário.
- Edição por arrastar/redimensionar.
