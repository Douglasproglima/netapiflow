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
  commit, seção 15.2 do `netapiflow-prompt.md`). **Isso não significa uma PR por commit**: várias
  mudanças pequenas e relacionadas (atualizar um status, ajustar um workflow, registrar a decisão
  que isso gerou) cabem na mesma branch, em commits separados, numa única PR. Abrir uma PR por
  commit multiplica rodada de CI e confirmação de merge sem necessidade (D-63 em diante).

## Proteção de branch

`main` e `develop` têm proteção configurada no GitHub (D-65): PR obrigatório para mergear, checks
`pr-lint`, `secret-scan`, `detect-changes` e `build-test` (os dois sistemas operacionais)
obrigatórios, sem force-push, sem apagar a branch. Vale até para quem administra o repositório.
Push direto numa das duas branches é rejeitado pelo próprio GitHub, não só pela convenção abaixo.

## Ações que exigem confirmação

Push, merge, tag, abrir ou mergear PR e criar release são visíveis publicamente no GitHub (o
repositório é público). Nunca fazer nenhuma delas sem confirmação explícita na conversa.
Confirmação vale para aquela ação, não para as próximas.

### Exceção: PR só de documentação (D-109)

Quando toda a mudança está em `README.md`, `README.pt-BR.md`, `CHANGELOG.md`, `docs/plano/`,
`docs/adr/` ou `.claude/`, sem nenhum arquivo em `src/`, `tests/`, `samples/` ou `ClientApp/`: não
perguntar confirmação em cada etapa (criar branch, empurrar, abrir PR, mergear). Juntar tudo numa
branch e numa PR só, esperar o CI (segundos, por causa do `docs_only` do `detect-changes`) e
mergear assim que ficar verde, sem pergunta extra de "posso mergear". Pedido explícito do usuário:
o processo de confirmar passo a passo é burocracia demais pra um tipo de mudança de baixo risco e
fácil de reverter.

Essa exceção nunca se estende a uma PR que também toque `src/`, `tests/`, `samples/` ou
`ClientApp/`, mesmo que a maior parte do diff seja documentação: nesse caso vale a regra geral
acima, sem exceção.

## Limpeza depois do merge

Rotina padrão assim que uma PR for confirmada como `MERGED` (sempre confirmar via `gh pr view
--json state,mergedAt` ou equivalente antes de apagar qualquer coisa, nunca assumir pelo nome ou
pela aparência do branch):

- Worktree isolado criado para aquele trabalho (padrão da Planejadora, ver seção acima):
  `git worktree remove <path> --force`.
- Branch remota da PR: conferir se ainda existe (`gh api repos/<owner>/<repo>/branches`). Se
  `--delete-branch` não rodou (instabilidade da API do GitHub já aconteceu nesta sessão: a
  mutação de merge foi processada mas a resposta falhou, deixando a flag de apagar branch sem
  efeito), apagar manualmente (`gh api -X DELETE repos/<owner>/<repo>/git/refs/heads/<branch>`).
- Checkout compartilhado (Executora): se o branch local dela aponta para uma PR já mergeada e o
  `git status` está limpo (sem nada além de scratch conhecido: screenshots, logs de verificação
  manual), trocar para `develop`, `git pull`, e apagar o branch local antigo (`git branch -d`).
  Nunca fazer isso com trabalho não commitado no meio.

## Nunca

- `git push --force` em `main`, `develop` ou `release/*`.
- Commit direto em `main` ou `develop`.
- Reescrever histórico já publicado.
- A Executora alterar `docs/plano/` ou `docs/adr/`: encontrar um conflito entre o plano e o
  `netapiflow-prompt.md` é motivo para parar e relatar, não para editar o plano sozinha
  (seção 16.6 do prompt).
