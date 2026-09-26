# #33 — Atalhos no agendamento

## Resultado esperado

As ações frequentes de uma consulta ficam disponíveis no próprio cartão sem abrir uma tela intermediária.

## Dependências

- Nenhuma alteração de backend.
- Reutilizar mutations, permissões e modais já existentes em `appointment-board.tsx`.

## Implementação

1. Inventariar os handlers atuais de confirmar, iniciar, concluir, falta, editar, cancelar, receber e prontuário.
2. Manter a próxima ação do fluxo como botão principal e agrupar as demais em menu `Mais ações`.
3. Derivar a visibilidade do status e das permissões existentes, sem duplicar autorização do servidor.
4. Reutilizar o mesmo componente de ações nos cartões das visões que suportam interação.
5. Desabilitar o conjunto enquanto uma mutation da consulta estiver pendente.

## Verificação

- Teste por status e permissão para a ação principal e o menu.
- Teste de chamada de cada handler existente.
- Teste de teclado, nome acessível e foco ao fechar modal/menu.

## Fora do escopo

- Atalhos globais de teclado.
- Novos endpoints ou sistema configurável de ações.
