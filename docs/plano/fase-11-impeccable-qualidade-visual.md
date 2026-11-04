# Fase 11: Impeccable como guarda de qualidade visual

Projeto: `src/NetApiFlow.Dashboard/ClientApp`. Também `.github/workflows/`, `CHANGELOG.md`. Nenhuma
mudança em `src/NetApiFlow` (núcleo) nem em qualquer outro pacote .NET.

Pré-requisito: nenhum funcional. Pode rodar em paralelo à Fase 10 (área de código diferente,
`NetApiFlow.Mcp`), ou depois dela; a Executora decide a ordem real conforme disponibilidade.

Registrado em ADR-0014. Contexto do detector já escrito pela Planejadora: `PRODUCT.md` e
`DESIGN.md`, na raiz do repositório. `docs/brand/tokens.md` já ganhou a seção de cor de texto
sobre fundo tingido (`-on-tint`), e os dois mockups de `docs/design/` já foram corrigidos para
usá-la, com o rótulo de versão em 11px. A Executora **não precisa refazer isso**, só aplicar o
equivalente no código real.

## Checagens antes de começar

- `PRODUCT.md`, `DESIGN.md`, `.claude/rules/design-quality.md` já existem na raiz/`.claude/rules/`
  (Planejadora). Ler antes de rodar o detector pela primeira vez.
- `docs/brand/tokens.md` já tem os valores corrigidos de `--naf-danger-on-tint`,
  `--naf-warning-on-tint`, `--naf-success-on-tint`, com a razão de contraste de cada par.

## Tarefas

### 11.1: Instalação e hooks

`npx impeccable install` (perfil Claude Code), `npx impeccable telemetry off`,
`npx impeccable hooks on`. Registrar no resumo da fase o que cada comando alterou (arquivo de
config criado, hook adicionado).

**Pronto quando:** os três comandos rodam sem erro, e o hook roda automaticamente numa edição de
teste em `ClientApp/`.

### 11.2: Tokens `-on-tint` no SCSS real

`src/NetApiFlow.Dashboard/ClientApp/src/styles/_tokens.scss` ganha `--naf-danger-on-tint`,
`--naf-warning-on-tint`, `--naf-success-on-tint`, mesmos valores e mesma lógica por tema do
`docs/brand/tokens.md` (reaproveitar a cor base onde a Planejadora não mediu falha). Toda classe
que hoje usa a cor base como texto sobre o próprio fundo tingido dela (badge de verbo HTTP,
chip de status, banner de erro, etiqueta de span) passa a usar o token `-on-tint`. Os dois
mockups já corrigidos (`docs/design/*/mockup.html`) servem de referência exata de quais classes
mudam.

**Pronto quando:** `npx impeccable detect` contra `/flow?trace=<id>`, `/history`, `/components` e
`/runner`, nos dois temas e em 390×844, não reporta mais `low-contrast` nos pares já medidos.
Mitiga o achado 1 do relatório original.

### 11.3: Tamanho mínimo de texto

Rótulo de versão abaixo do logo (e qualquer outro texto de interface abaixo de 11px) sobe para
11px, mesmo valor já aplicado nos mockups.

**Pronto quando:** `npx impeccable detect` não reporta mais `undersized-ui-text`.

### 11.4: Hierarquia tipográfica, alinhar à escala do guia (não inventar tamanho novo)

**Achado real, não um aviso a calar**: a implementação divergiu da escala já definida em
`docs/design/flow-workspace/guide.md`, seção 3.3. O mockup segue essa escala e passa no
detector; o app real não. Corrigir a implementação para bater exatamente com a tabela do guia,
em especial:

- Título de card (ex. "Caminho da requisição", "Execuções recentes", "Eventos ao vivo", "Testar
  um endpoint"): `Inter`, 14px/20, peso 600 (hoje maior, por isso o h1 some visualmente perto
  disso).
- Corpo, itens de lista: `Inter`, 13px/20 (hoje 14px).
- Título da página (topbar): `Plus Jakarta Sans`, 18px/24, peso 700. **Não muda.**

Conferir a tabela completa da seção 3.3 do guia para qualquer outro papel tipográfico que também
tenha divergido (valor de métrica, rótulo/legenda, métrica pequena, badge de verbo), não só os
dois citados aqui.

**Pronto quando:** `npx impeccable detect` não reporta mais `flat-type-hierarchy`, e uma conferência
visual manual (print ou descrição no resumo da fase) mostra título de card visivelmente menor que
título de página, igual ao mockup.

### 11.5: `nested-cards` na caixa de erro do painel de detalhes

`flow-details__errbox` hoje tem aparência de card dentro de outro card (o próprio painel de
detalhes). Ajustar para uma seção com destaque plano (ex. fundo tingido sem borda arredondada
igual à do card pai, ou remover a borda/sombra duplicada), não um card aninhado.

**Pronto quando:** `npx impeccable detect` não reporta mais `nested-cards` nessa tela.

### 11.6: Exceções conscientes da marca

`npx impeccable ignores add-value ai-color-palette <valor-da-cor-flow> --reason "ADR-0009, ADR-0014"`
e `npx impeccable ignores add-value overused-font Inter --reason "ADR-0009, ADR-0014"` (ou a
sintaxe real da versão instalada, confirmar com `npx impeccable ignores --help`). Preferir
`add-value` a `add-rule`: a regra continua ativa para qualquer cor ou fonte que não seja a da
marca.

**Pronto quando:** `npx impeccable detect` não reporta mais `ai-color-palette` nem
`overused-font` para os valores da marca, e reportaria normalmente para um valor diferente (teste
manual: trocar temporariamente uma cor por outro roxo fora da paleta e confirmar que o detector
ainda acusa).

### 11.7: Duas divergências achadas fora do detector

- Botão "Testar um endpoint" hoje solto no canto do painel do grafo; no mockup fica na barra do
  topo (`docs/design/flow-workspace/mockup.html`). Mover para lá.
- Barras de duração da lista de etapas quase não aparecem no app real. Confirmar com um sample
  rodando de verdade (não dado simulado): se for bug real (ex. escala de duração errada, cor sem
  contraste suficiente para aparecer), corrigir; se for só efeito de dado simulado sem variação
  de duração suficiente, documentar no resumo da fase sem mudar código.

**Pronto quando:** botão na barra do topo, igual ao mockup; comportamento das barras de duração
confirmado com dado real, corrigido ou explicado.

### 11.8: CI no GitHub Actions

Novo step (ou job) em `.github/workflows/ci.yml` (só quando o PR toca `ClientApp/`, reaproveitando
o `detect-changes` já existente, D-64): subir um dos `samples/` com o dashboard habilitado,
gerar algumas requisições reais contra ele (incluindo uma que force erro), rodar
`npx impeccable detect --github` contra `/flow?trace=<id>`, `/history`, `/components` e
`/runner`, com `--viewport 390x844` também. Registrar como risco em `docs/plano/riscos.md`
(a Planejadora já abre o número do risco antes de a Executora começar, ver R-15): container
rodando como root pode exigir `--no-sandbox` para o Chromium do detector; o runner padrão do
GitHub Actions não deveria precisar disso, mas documentar a bandeira mesmo assim, para quem rodar
localmente em container.

**Pronto quando:** o job roda verde numa PR real, falha de propósito numa PR com uma regressão
visual forçada (teste manual antes de reverter), e documenta no resumo da fase se
`--no-sandbox` foi necessário no runner do GitHub ou não.

### 11.9: Comandos limpos e CHANGELOG

`npm run lint`, `npm run test:ci`, `npm run build`, `npm run format:check` em `ClientApp/`;
`dotnet build` e `dotnet test NetApiFlow.slnx` nos dois TFMs. `CHANGELOG.md` ganha uma entrada
`[Unreleased]` (ou a próxima versão, conforme o estado do repositório na hora) descrevendo a
correção de contraste e a adoção do Impeccable como guarda de CI.

**Pronto quando:** todos os comandos limpos, `CHANGELOG.md` atualizado.

## Aceite da fase

Todos os critérios de pronto acima. Contagem do detector no resumo de fim de fase: quantos avisos
antes da fase, quantos depois (esperado: zero fora das duas exceções conscientes). R-15 mitigado
e provado (ou "não aplicável" documentado, se o runner do GitHub realmente não precisar de
`--no-sandbox`).

## Ao terminar

Resumo para a Planejadora: o que foi feito, contagem do detector antes/depois, se
`--no-sandbox` foi necessário no CI, o que ficou pendente. A Planejadora roda `/revisar-fase 11`
antes de liberar a próxima fatia de trabalho.
