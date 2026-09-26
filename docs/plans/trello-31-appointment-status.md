# #31 — Cores e mudança de status

## Resultado esperado

O status fica identificável por texto e cor, e a equipe executa diretamente apenas a próxima transição válida.

## Dependências

- Nenhuma alteração de API, entidade ou máquina de estados.
- Fonte de verdade: `spec/state-machines.yaml`.

## Implementação

1. Consolidar as variantes de consulta em `src/components/ui/status-badge.tsx`.
2. Remover mapeamentos de cor duplicados de `appointment-board.tsx`, usando o resolvedor compartilhado também nas bordas dos cartões.
3. Mapear status atual para os endpoints permitidos: confirmar, iniciar, concluir, falta e cancelar.
4. Exibir rótulo, carregamento, sucesso e erro; invalidar a query da agenda após sucesso.
5. Manter ações finais ocultas para `Completed`, `Cancelled` e `NoShow`.

## Verificação

- Teste parametrizado para rótulo/variante de cada status.
- Teste das ações disponíveis em cada estado.
- Teste acessível garantindo texto visível sem depender da cor.

## Fora do escopo

- Arrastar cartões para alterar status.
- Endpoint genérico de status.
