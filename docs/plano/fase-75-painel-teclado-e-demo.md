# Fase 75: o painel, o teclado e a demo (D-320, grupo D da `0.7.0`)

**Estado (26/11/2026):** planejada, não iniciada. Abre **depois** de a auditoria fechar a Fase 74 (D-320).

A Fase 68 fez só o essencial do teclado: foco visível, a gaveta como diálogo e o foco que caía no `body`. Esta fase faz o
resto, mais três ajustes do painel e um buraco da demo pública: ela não tem nenhuma execução que pule um passo, então o nó
"Não executado", a função central da visão de fluxo, nunca aparece nela.

A `0.7.0` **não quebra a API pública** (D-320). Esta fase mexe no `ClientApp`, no sample e nas fixtures da demo, e não
acrescenta nem remove linha do `PublicAPI`.

## Os achados e o que a Planejadora conferiu

| # | Item do roadmap | Conferi |
|---|---|---|
| 39 | O resto do teclado: setas no `radiogroup`, link de pular para o conteúdo, `Esc` no painel da etapa, região `live` | Li a Fase 68 (o que ela fez e o que deixou) e a D-236. Não testei o teclado |
| 60 | Levar o foco do teclado ao título da tela nova em toda troca de rota. Hoje, abrir um trace por um link (uma linha do Histórico, "Ver todas") troca a rota e o foco volta ao topo, como numa carga completa | Achado da Executora na Fase 68 (D-288). Quem usa o teclado passa por toda a barra de cima a cada navegação |
| 40 | O painel mostrar o erro de schema ausente (hoje só o log diz) | Li o item (D-236) |
| 72 | A tela Arquitetura escreve "1 execuções nesta janela": o plural em português para o número 1 | Achado da auditoria na `0.6.0` (D-315). A correção é a regra do singular nos dois idiomas |
| 49 | A demo gravada não tem nenhuma execução que pule um passo: nas 20 traces com blueprint anterior, o blueprint tem os mesmos passos da trace. Pede um endpoint de retorno antecipado no sample, gravado na regeneração das fixtures | Auditoria de 21/11/2026 (D-252). **Mexe em código do sample**, por isso não coube na regeneração do release |

O item 62 (a corrida do formulário do Runner) **não entra**: não foi reproduzido em 100 tentativas. Volta só se alguém o
reproduzir com um backend real (D-320).

## Ponto de parada

- **Cor, tipografia e espaçamento vêm de `docs/brand/tokens.md`** (ADR-0009, `design-quality.md`). Se um valor que a
  correção precisa não existir lá, a Executora **para** e a Planejadora o escreve. Valor inventado para calar o detector é
  proibido (D-76).
- O detector (`npx impeccable detect`) bloqueia só `low-contrast` e `undersized-ui-text`. Se uma correção fizer uma das duas
  aparecer, para e relata.
- **Item 49:** o endpoint novo do sample muda a demo pública, e a demo do site (`netapiflow-site`) só é refeita no release,
  da tag (como na 62.4). A Executora **não** mexe no repositório do site. Se o endpoint exigir um `Flow.Step` ou uma forma
  de marcar que o sample ainda não usa, para e relata.
- O foco nunca pode ser roubado: se mover o foco ao título atrapalhar quem navega por link com o teclado, ou colidir com a
  gaveta da Fase 68, para e relata.

## Tarefa 75.0: reproduzir e medir, antes de corrigir

Em navegador de verdade, só com o teclado, nos dois temas, no desktop e no `390x844`, no painel e na demo estática:

1. **39.** Hoje, para cada um dos quatro pontos: o que o `Tab` e as setas fazem no `radiogroup`, se existe o link de pular,
   o que o `Esc` faz no painel da etapa, e se a mudança de estado (uma trace nova, um erro) chega a um leitor de tela.
2. **60.** Onde o foco está depois de cada troca de rota (Histórico para o trace, "Ver todas", e os links entre as quatro
   telas).
3. **40.** Um host com `AutoCreateSchema = false` e sem o schema: o que o painel mostra e o que só o log diz.
4. **72.** Todo contador do painel que usa plural, nos dois idiomas: a lista dos que erram com 0 e com 1.
5. **49.** Em `tests/` e nas fixtures da demo: quantas traces, quantas com blueprint anterior, e que nenhuma pula um passo.

## Tarefa 75.1: o teclado (itens 39 e 60)

- **Setas no `radiogroup`:** as setas movem a seleção, `Tab` entra e sai do grupo, no padrão WAI-ARIA do `radiogroup`.
- **Link de pular:** o primeiro elemento focável da página leva ao conteúdo principal, visível só no foco.
- **`Esc` no painel da etapa:** fecha o painel e devolve o foco a quem o abriu, como a gaveta da Fase 68.
- **Região `live`:** o que muda sem ação do usuário (uma trace nova, um erro, "Aguardando") é anunciado, sem repetir a cada
  evento do stream. Um teste prova que a região não anuncia a cada tick.
- **Foco na troca de rota (item 60):** depois de cada troca de rota, o foco vai ao título da tela nova. Na carga inicial, nada
  muda.

## Tarefa 75.2: o painel e o plural (itens 40 e 72)

- **40.** O painel mostra o erro de schema ausente de um jeito que quem usa o painel entende e que diz o que fazer (o texto
  do erro do storage já aponta o `schema.sql`), nos dois idiomas.
- **72.** O plural em português segue a regra do singular nos dois idiomas, em **todos** os contadores que a 75.0 listou,
  e não só na Arquitetura. Teste de componente com 0, 1 e 2.

## Tarefa 75.3: a demo que pula um passo (item 49)

1. Um endpoint de retorno antecipado no sample (por exemplo, "cliente não encontrado" antes de chamar o repositório), de
   modo que o blueprint tenha um passo que a trace não executa.
2. As fixtures da demo regeneradas por esse endpoint. A visão de fluxo da demo mostra o nó "Não executado".
3. O roteiro de verificação das fixtures atualizado, e a prova no navegador (captura de tela) nos dois temas.
4. A demo publicada só muda no release (a Tarefa 62.4 refaz a demo da tag). A Executora diz isso no relatório.

## Fora de escopo

- O item 62 (a corrida do formulário do Runner), até alguém reproduzir.
- Qualquer redesenho, tela nova ou texto de marketing.
- O repositório do site.
- O grupo B (a API pública).

## Critério que vale para a fase inteira

- `npm run lint`, `npm run format:check`, `npm run test:ci` e `npm run build` limpos no `ClientApp`, e `dotnet build`,
  `dotnet test` e `dotnet format` nos dois TFMs (o sample muda).
- Cada ponto do teclado **visto falhando antes**, com captura de tela, e passando depois, nos dois temas. O roteiro de
  Playwright, só com o teclado, fica em `tests/manual-verification/` como o da Fase 68.
- O detector nas telas afetadas, nos dois temas e no desktop e no `390x844`, com a **contagem de avisos antes e depois**
  (`design-quality.md`). Zero `low-contrast` e zero `undersized-ui-text`.
- Cada correção com a linha no `tests/regression-guards/guards.json`.
- `CHANGELOG.md`, em inglês: `### Fixed` para o 60, o 40 e o 72, `### Added` para o resto do teclado (item 39).

## Versionamento e fluxo

- `0.7.0`. Branch `feature/75-painel-teclado-e-demo` a partir de `develop` atualizado, depois da Fase 74 fechada. Pode sair
  em PRs separadas (o teclado, o painel, a demo). Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e
  parar.
- A auditoria fecha a fase com o pacote real, todas as telas nos dois temas e em 390, e o núcleo fixo do D-318.

## Relatório final

O roteiro do teclado antes e depois, nos dois temas. A contagem de avisos do detector. A captura da demo com o nó "Não
executado". A lista de contadores com o plural que a 75.0 achou.
