# Decisões que esperam o dono

Arquivo vivo. A Planejadora prepara as opções e diz o que recomenda, mas **não decide** o que está aqui.
Quando o dono decide, a decisão vira uma linha em `decisoes.md` e o item sai deste arquivo.

As quatro decisões da rodada de 20/11/2026 (veículo da Fase 50, TFMs, divulgação e o README na `main`)
já foram tomadas e estão em D-188, D-189, D-190 e D-191. A escolha sobre os workflows do hotfix `0.5.2`
também: D-194.

A data de saída do `net8.0` foi confirmada no D-195.

A delegação à auditoria e o segundo uso da exceção D-191 estão no D-198. Se o dono quiser corrigir a
leitura do "eu" da opção "Pode executar", é aqui que volta a aparecer.

## Alcance do D-201 e o texto de `.claude/rules/git-workflow.md`

A PR #344 tirou `git merge`, `git push` e `gh pr create` do `ask` no `.claude/settings.json`. A seção
"Ações que exigem confirmação" de `git-workflow.md` ainda diz que push e abertura de PR pedem
confirmação explícita na conversa, e a Planejadora não mudou o texto por conta própria.

A `AUDITORIA-16-11` relatou o texto do dono (20/11/2026, na sessão dela): "afrouxe a regra para essas
duas sessões", e, entre as opções que ela apresentou, "Só o trabalho do dia (Recomendado)", que libera
`git merge` local, `git merge-tree`, `git push` de branch e `gh pr create`, e deixa `gh pr merge`,
`git tag` e `gh release` com o dono. A Planejadora não leu esse texto na sessão do dono, e uma sessão
par não pode afrouxar a regra que a governa. Por isso `git-workflow.md` fica como está até o dono
confirmar aqui, ou editar o arquivo ele mesmo.

- **Recomendação da Planejadora:** confirmada a leitura acima, alinhar `git-workflow.md` ao texto do
  dono: push de branch e abertura de PR são livres para a Planejadora e a Executora, e merge, tag,
  Release e publicação de pacote seguem com a auditoria e o dono.

## O item 73 do roadmap na Fase 72 (D-323)

O `SQLitePCLRaw` precisa de uma linha no `ignore` do `dependabot.yml`, só para a major, e isso é achado novo depois do
congelamento da `0.7.0` (D-320). Pelo congelamento, vai para o roadmap como item 73 e espera a versão seguinte.

- **Recomendação da Planejadora:** pôr o item 73 na Tarefa 72.2, junto do item 70. É uma linha no mesmo bloco do mesmo
  arquivo, a Executora já vai mexer ali, e o pacote não muda. Se o dono aceitar, a Planejadora acrescenta o item à Fase 72.
