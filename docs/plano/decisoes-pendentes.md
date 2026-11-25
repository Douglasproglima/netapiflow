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

## O comentário no código: o `CLAUDE.md` do projeto e a regra global do dono se contradizem

O `CLAUDE.md` do repositório diz, na regra de ouro, que comentário só existe no último caso, para um porquê que nenhum nome resolve (uma decisão contraintuitiva, uma restrição externa, uma condição de corrida, uma fonte de pesquisa). A regra global do dono (`~/.claude/CLAUDE.md`) diz que não se escreve comentário nenhum, que o fato que não cabe no código vai para a mensagem de commit, para um teste com nome descritivo ou para `docs/`, e que ela está acima do `CLAUDE.md` de projeto.

Na 59B (D-300) a auditoria aplicou a global: os três blocos de `//` que sobravam viraram teste, e `src/**/*.cs` não tem mais nenhum `//`. A Planejadora tinha aplicado a do projeto e mudou. Falta o dono dizer qual vale para este repositório, porque quem lê o `CLAUDE.md` do projeto (um colaborador, outra IA) lê uma regra mais folgada do que a que se cumpre.

- **Opção A, alinhar o `CLAUDE.md` do projeto à global:** nenhum comentário, e o porquê vai para um teste com nome descritivo, para o commit ou para `docs/`. É o que a 59B já fez no código.
- **Opção B, manter o `CLAUDE.md` como está, e a global só para as sessões do dono:** o repositório fica com a regra mais folgada para quem contribui, e as sessões dele seguem a estrita.
- **Recomendação da Planejadora:** a opção A. Uma regra que o repositório descreve e que o código não cumpre confunde quem lê, e a 59B mostrou que os porquês cabem em testes. Quem edita o `CLAUDE.md` é o dono.
