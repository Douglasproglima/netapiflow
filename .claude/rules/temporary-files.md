# Diretórios e recursos temporários: quem cria, remove

Regra do dono (21/11/2026) para as três sessões: a Planejadora, a Executora e a auditoria. O disco da máquina está
acabando, e cada worktree, app de sonda ou `node_modules` pesa de dezenas a centenas de MB (um worktree com o build do
painel passou de 300 MB, e outro, de trabalho, de 1,4 GB).

## A regra

**Quem cria um diretório ou um recurso temporário o remove assim que não precisar mais dele**, sem esperar pedido. Antes
de dizer que terminou uma tarefa, lista o que criou e remove. Se algo ficar de propósito, o relatório diz o quê, onde,
quanto pesa e até quando.

## O que conta como temporário

- **Worktrees**, os de `.claude/worktrees/` e os de scratchpad: `git worktree remove --force <caminho>` e, só depois de
  um `remove` que terminou sem erro, `git worktree prune`. Nunca apagar a pasta por fora, porque o Git fica com a entrada.
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

## O que se mediu em 21/11/2026 sobre remover worktree

- **Antes do `remove`, rodar `dotnet build-server shutdown`.** Os servidores do compilador e do MSBuild podem segurar arquivos abertos. Um `remove` feito sem isso falhou com `Permission denied` no meio, e três feitos com isso saíram limpos. Não é garantia: um quarto, feito com isso, deixou uma pasta vazia.
- **Sair da pasta do worktree numa chamada anterior ao `remove`.** O Windows trava a pasta em que o shell está, e o Git para no meio.
- **Se o `remove` falhar, não rodar `prune`.** O Git já apagou o `.git` do worktree, e o `prune` o desregistra: a pasta fica sem dono e não dá mais para repetir o `remove`. Sair da pasta e repetir o `remove`.
- **`tests/manual-verification/` restaura centenas de MB dentro do worktree.** O publish AOT baixa 451 MB de runtime packs para `./local-packages`. Rodar essa verificação num worktree só para ela, e removê-lo ao terminar.

## Docker

- **Nunca `docker volume prune`.** Ele apaga os volumes de todos os projetos da máquina, não só os nossos.
- **Volume solto só se apaga depois de provar de quem é.** Montá-lo somente leitura num contêiner descartável e ler o conteúdo (para um banco PostgreSQL, o `PG_VERSION` e o catálogo com o nome dos bancos). Apagar um a um, sem `-f`.
- **Imagem é cache compartilhado.** Só sai com o dono dizendo que sim, e nunca a que não volta por `docker pull`, como um build local.

## Como evitar o problema

- Criar o diretório temporário num lugar só, o scratchpad da sessão, com um nome que diga de quem é e para quê.
- Reaproveitar um worktree em vez de criar outro, e um build de `dist` já feito em vez de refazê-lo.
- Ao fechar a revisão de uma PR, remover o worktree dela no mesmo turno.
