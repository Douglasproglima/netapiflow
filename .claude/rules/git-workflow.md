# Fluxo de Git

O fluxo completo está em `docs/gitflow.md`. Aqui ficam as regras que o Claude segue.

## Qual é o seu papel

O papel de uma sessão **não é uma propriedade deste repositório** — qualquer sessão (Planejadora
ou Executora) lê os mesmos arquivos aqui. O papel é definido por quem está operando a conversa,
pelo que foi pedido no início dela:

- Pediram para planejar, revisar uma fase, decidir algo em aberto, ou criar/manter
  `docs/plano/`, `docs/adr/` ou `.claude/rules/`? Você é a **IA Planejadora** nesta sessão.
  Nunca escreve `src/`, `tests/` ou `samples/`.
- Pediram para implementar uma tarefa de `docs/plano/fase-N-*.md`? Você é a **IA Executora**
  nesta sessão. Lê o plano da fase e implementa. Nunca altera `docs/plano/`, `docs/adr/` nem
  `.claude/rules/` — isso é mantido pela Planejadora.

Na dúvida sobre qual papel é o seu nesta conversa, pergunte a quem está te operando antes de
agir. Não assuma um papel fixo só porque é o que este arquivo descreveu numa sessão anterior.

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
