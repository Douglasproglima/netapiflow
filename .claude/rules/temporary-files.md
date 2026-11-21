# Diretórios e recursos temporários: quem cria, remove

Regra do dono (21/11/2026) para as três sessões: a Planejadora, a Executora e a auditoria. O disco da máquina está
acabando, e cada worktree, app de sonda ou `node_modules` pesa de dezenas a centenas de MB (um worktree com o build do
painel passou de 300 MB, e outro, de trabalho, de 1,4 GB).

## A regra

**Quem cria um diretório ou um recurso temporário o remove assim que não precisar mais dele**, sem esperar pedido. Antes
de dizer que terminou uma tarefa, lista o que criou e remove. Se algo ficar de propósito, o relatório diz o quê, onde,
quanto pesa e até quando.

## O que conta como temporário

- **Worktrees**, os de `.claude/worktrees/` e os de scratchpad: `git worktree remove --force <caminho>` e depois
  `git worktree prune`. Nunca apagar a pasta por fora, porque o Git fica com a entrada.
- **Apps e projetos de sonda, de reprodução e de verificação**, no scratchpad ou em `%TEMP%`: a pasta inteira, com `bin`,
  `obj` e `node_modules`.
- **A saída de `dotnet pack` de verificação** (`local-nuget-feed` e `local-packages` de `tests/manual-verification/`) e os
  pacotes `-verify.<n>` do cache, depois de a verificação fechar.
- **Contêineres, volumes e redes do Docker** criados para uma medição: `docker rm -f` e `docker volume rm`. As imagens
  baixadas são cache compartilhado e ficam.
- **Cópias de `dist`, logs grandes e bancos de dados** gerados na verificação.

## O que nunca se remove

- O que outra sessão criou e ainda usa. Pede-se a quem criou, por mensagem.
- Um worktree com arquivo modificado ou com commit sem enviar.
- O que o dono marcou para ficar.

## Quando a remoção é negada

A permissão de `rm -rf` está negada nesta máquina. **Não contornar** com outro comando equivalente (`find -delete`,
`cmd rmdir` e parecidos). Diz-se ao dono o caminho exato e o peso, e pede-se que ele apague ou libere a permissão. Os
worktrees se removem pelo Git, que a permissão permite.

## Como evitar o problema

- Criar o diretório temporário num lugar só, o scratchpad da sessão, com um nome que diga de quem é e para quê.
- Reaproveitar um worktree em vez de criar outro, e um build de `dist` já feito em vez de refazê-lo.
- Ao fechar a revisão de uma PR, remover o worktree dela no mesmo turno.
