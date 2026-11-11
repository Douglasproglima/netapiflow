# Fase 22: Diff entre execuções

Projeto tocado: só `src/NetApiFlow.Dashboard/ClientApp`. Nenhuma mudança em
`src/NetApiFlow`, `ITraceStore` ou `docs/plano/contratos.md`.

Item 1 do roadmap de valor (D-58 item 4, D-99), depois da Fase 21. Dor documentada em D-58:
comparar duas execuções da mesma rota lado a lado, hoje só dá pra abrir duas abas e comparar de
cabeça.

Pesquisa da Planejadora confirmou que o produto já resolveu a parte mais arriscada deste
problema, duas vezes: `findNPlusOneGroup` (`graph-builder.ts`, Fase 9) e o nó "não executado" do
`GET /blueprint` (atual vs. última execução bem-sucedida) já casam spans de execuções diferentes
pela chave `${component}::${operation}`, nunca por `spanId` (um `spanId` é único por execução,
nunca se repete entre duas). Esta fase generaliza esse casamento: em vez de "atual vs. última bem-
sucedida", o usuário escolhe quaisquer duas execuções pra comparar.

## Tarefa 22.1: escolher duas execuções em `/history`

`/history` ganha um jeito de marcar exatamente duas execuções (ex. checkbox por linha, decisão de
UI da Executora) e um botão "Comparar" que só fica ativo com exatamente duas marcadas. Reforça
visualmente (aviso, não bloqueio) quando as duas marcadas não são da mesma rota
(`method`/`routeTemplate` diferentes): a comparação ainda funciona (tudo aparece como "só existe
numa das duas"), só não é o caso de uso principal que D-58 pediu.

Navega pra uma página nova, `/diff?a=<traceIdA>&b=<traceIdB>`.

**Pronto quando:** marcar duas execuções em `/history` e clicar "Comparar" abre `/diff` com os
dois `traceId` certos na URL. Marcar uma terceira desmarca a mais antiga das duas (nunca mais de
duas marcadas ao mesmo tempo).

## Tarefa 22.2: página `/diff`

Busca as duas execuções via `ApiService.getTrace(traceId)` (chamado duas vezes, mesma forma
`FlowTraceDetail` de sempre, nenhuma rota nova). Função pura nova,
`features/flow/diff/trace-diff-builder.ts` (sem import de Angular, sem `HttpClient`, testável sem
`TestBed`, mesmo padrão de `graph-builder.ts`, `.claude/rules/frontend.md`), casando os spans das
duas execuções pela chave `${component}::${operation}` (reaproveitada de `graph-builder.ts`,
nunca reimplementada com um formato ligeiramente diferente).

Resultado da função: pra cada chave `component::operation`, três casos possíveis: presente nas
duas (`matched`, com a duração de cada lado e o delta), só na execução A (`onlyInA`), só na
execução B (`onlyInB`). Repetição do mesmo `component::operation` dentro da mesma execução (N+1):
soma por chave, mesmo critério de agregação que `findNPlusOneGroup` já usa pra achar o grupo,
reaproveitado, não reinventado.

Layout lado a lado (duas colunas ou duas waterfalls espelhadas, decisão de UI da Executora).
`onlyInA`/`onlyInB` reaproveitam a linguagem visual já estabelecida pra "não executado"
(`NodeVisualState`, a mesma usada pelo nó fantasma do blueprint): cor + ícone + texto, nunca só
cor (seção 7.2 do prompt, regra já em `.claude/rules/frontend.md`). `matched` mostra o delta de
duração (A mais rápida, B mais rápida, ou igual dentro de uma margem pequena, decisão de UI).

Cabeçalho da página mostra as duas execuções identificadas (rota, método, status, horário), pra
quem está olhando saber qual é qual sem precisar decorar a ordem de A/B.

**Pronto quando:** comparar duas execuções da mesma rota com uma etapa a mais numa delas (ex. uma
teve um N+1 que a outra não teve) mostra essa etapa como `onlyInA`/`onlyInB`, nunca como
`matched` com duração zerada. Comparar a mesma execução contra ela mesma (`a` e `b` iguais)
mostra tudo como `matched`, delta zero em todo mundo. `trace-diff-builder.spec.ts` cobre os três
casos (`matched`, `onlyInA`, `onlyInB`) e o caso de N+1 dentro da mesma execução, com cobertura
mínima de linha equivalente à exigida pra `graph-builder` (D-10, 80%).

## Fora de escopo, de propósito

- Comparar mais de duas execuções ao mesmo tempo: YAGNI até aparecer um pedido real.
- Qualquer mudança em `GET /blueprint` ou no mecanismo de nó "não executado" do `/flow` ao vivo:
  continuam existindo exatamente como estão, esta fase não os substitui nem reimplementa, só
  reaproveita a mesma chave de casamento.
- Persistir ou compartilhar uma comparação (ex. link direto pra um diff salvo): a URL
  `/diff?a=...&b=...` já é compartilhável por si só, enquanto as duas execuções ainda existirem
  no `ITraceStore` de quem abre o link; nenhum mecanismo novo de persistência.

## Aceite da fase

Critérios de pronto das duas tarefas acima. `npm run lint`, `npm run format:check`,
`npm run test:ci`, `npm run build` limpos. Detector Impeccable rodado em `/history` e `/diff`
(tema claro/escuro, desktop e `390x844`), contagem de avisos antes/depois no resumo de fim de
fase.

## Ao terminar

Resumo pra Planejadora: o que foi feito, como verificar (passo a passo manual: marcar duas
execuções reais do sample, uma com erro ou N+1, comparar, conferir visualmente os três casos), o
que ficou pendente. A Planejadora roda `/revisar-fase 22` antes de decidir o próximo item do
roadmap.
