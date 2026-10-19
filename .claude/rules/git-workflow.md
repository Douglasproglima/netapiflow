# Fluxo de Git

O fluxo completo está em `docs/gitflow.md`. Aqui ficam as regras que o Claude segue.

## Papel fixo nesta sessão

Esta sessão é sempre a **IA Planejadora**: escreve e mantém `docs/plano/`, `docs/adr/` e
`.claude/rules/`. Nunca escreve `src/`, `tests/` ou `samples/`: isso é trabalho da IA Executora,
em outra sessão/ferramenta, que lê `docs/plano/fase-N-*.md` e implementa.

## Antes de editar

- Conferir a branch atual com `git branch --show-current`.
- Nunca editar direto em `main` ou `develop`. Se estiver numa delas, criar uma branch antes
  (`chore/*` para governança, `feature/*`/`bugfix/*` para o que a Executora fizer).
- `feature/*`, `bugfix/*` e `chore/*` sempre nascem de `develop` atualizado (`git pull` antes).

## Commits

- Conventional Commits, em inglês, uma linha, escopo opcional (`feat(core): ...`).
- Sem corpo desnecessário. Sem linha de coautor em commit fora de `main`.
- Commit pequeno e coerente. Uma mudança lógica por commit (uma tarefa do plano de fase = um
  commit, seção 15.2 do `netapiflow-prompt.md`).

## Ações que exigem confirmação

Push, merge, tag, abrir ou mergear PR e criar release são visíveis publicamente no GitHub (o
repositório é público). Nunca fazer nenhuma delas sem confirmação explícita na conversa.
Confirmação vale para aquela ação, não para as próximas.

## Nunca

- `git push --force` em `main`, `develop` ou `release/*`.
- Commit direto em `main` ou `develop`.
- Reescrever histórico já publicado.
- A Executora alterar `docs/plano/` ou `docs/adr/`: encontrar um conflito entre o plano e o
  `netapiflow-prompt.md` é motivo para parar e relatar, não para editar o plano sozinha
  (seção 16.6 do prompt).
