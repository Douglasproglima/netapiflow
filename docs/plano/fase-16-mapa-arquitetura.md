# Fase 16: Mapa de arquitetura vivo

Projetos tocados: `src/NetApiFlow` (núcleo, `ITraceStore`), `src/NetApiFlow.Storage.PostgreSql`,
`src/NetApiFlow.Storage.SqlServer`, `src/NetApiFlow.Dashboard` (API REST) e
`src/NetApiFlow.Dashboard/ClientApp` (página nova, reaproveitando o grafo SVG já existente).

Pré-requisito: Fase 3 aceita (`ITraceStore`, padrão a seguir) e Fase 5 (`/blueprint`, mesma dupla
`method`/`routeTemplate` e mesma régua de 400 sem os dois). Item E do roadmap D-60, topo do
ranking de valor de entrega depois da Fase 15 (D-83).

Contrato congelado em `docs/plano/contratos.md`: `ArchitectureNode`, `ArchitectureEdge`,
`ArchitectureMap`, `ITraceStore.GetArchitectureMapAsync`, `GET /architecture`. Decisão de escopo
(mapa por rota, não global cross-rota; "vivo" é sobre o dado, não sobre atualização automática)
travada em D-83, não renegociável dentro desta fase.

## Contexto (ler antes de implementar)

"Vivo" qualifica o **mapa**, não o grafo: gerado fresco a cada consulta, a partir de dado real
armazenado, nunca mantido à mão (ao contrário de um diagrama de arquitetura desenhado uma vez e
esquecido). O grafo em si é **estático**: uma consulta, uma resposta, sem atualização automática
via SSE conforme trace nova chega. Não confundir os dois. Se o usuário real pedir atualização ao
vivo depois, é uma extensão de fase nova, não um ajuste silencioso desta.

É a generalização do `/blueprint` que já existe (Fase 5): `/blueprint` infere passo não executado
a partir da **última** execução bem-sucedida de uma rota. Esta fase faz o mesmo tipo de pergunta
("o que essa rota normalmente faz"), mas sobre a união de **todas** as execuções de uma janela de
tempo, não só a última, e devolve o grafo completo (nó + aresta + percentual de ocorrência), não
só uma lista de spans.

**Regra de agregação:**
- Nó: um `(Component, Operation)` distinto observado em qualquer execução da rota na janela.
  `ExecutionCount` é quantas dessas execuções passaram por aquele nó.
- Aresta: uma relação pai-filho distinta observada entre dois spans da mesma trace (mesmo
  `ParentSpanId`/`SpanId`). `CallCount` é quantas vezes essa relação foi observada.
- O nó sem pai (span raiz da trace, tipicamente `Controller`/`Endpoint`) sempre tem
  `ExecutionCount == TotalExecutions`: toda execução completa da rota passa por ele.
- `TotalExecutions` é o total de execuções da rota na janela, mesmo denominador para calcular o
  percentual de qualquer nó/aresta (`ExecutionCount`/`CallCount` dividido por `TotalExecutions`).
  O cálculo do percentual é do frontend, não da API (mesmo tratamento de `errorRate` em
  `/metrics`).

## Fora de escopo, de propósito (D-83)

- Nenhum grafo cruzando rotas diferentes: o mapa é sempre de uma rota (`method`+`routeTemplate`),
  mesma dupla de `/blueprint`. Um mapa "de todo o sistema" é uma ideia diferente, não pedida por
  D-60 item E, e multiplicaria a ambiguidade de agregação (duas rotas diferentes podem chamar o
  mesmo componente de formas diferentes).
- Nenhuma atualização automática via SSE: o mapa é recalculado a cada `GET /architecture`, sempre
  que o frontend pedir de novo (ex.: ao trocar a janela de tempo), nunca via stream.
- Nenhuma mudança em `flow-graph.component` (o renderer SVG existente): um construtor de grafo
  novo traduz `ArchitectureMap` para o `GraphNode`/`GraphEdge` que o componente já consome, sem
  precisar de campo novo nem alteração no componente.
- Export de Mermaid/SVG é só frontend, sem endpoint novo: os dois reaproveitam o
  `ArchitectureMap` já carregado pela página.

## Tarefas

### 16.1: `ArchitectureNode`/`ArchitectureEdge`/`ArchitectureMap` e `GetArchitectureMapAsync` no núcleo

Novos `src/NetApiFlow/ArchitectureNode.cs`, `ArchitectureEdge.cs`, `ArchitectureMap.cs`, forma
congelada em `contratos.md`. `ITraceStore` ganha `GetArchitectureMapAsync(string method, string
routeTemplate, StatsQuery query, CancellationToken cancellationToken)`.

`InMemoryTraceStore`: filtra `_entries.Values` por `method`/`routeTemplate` e pela janela de
`query`, monta um dicionário `spanId -> (Component, Operation, Type)` por trace, percorre os
spans com `ParentSpanId` não nulo resolvendo o pai **dentro da mesma trace**, agrega nó e aresta
por chave `(Component, Operation)`/`(FromComponent, FromOperation, ToComponent, ToOperation)`.

**Pronto quando:** teste unitário cobre: trace única vira grafo simples (nó raiz + filhos diretos,
`ExecutionCount`/`CallCount` todos iguais a 1); duas execuções com o mesmo caminho dobram as
contagens, sem duplicar nó/aresta; execuções com caminhos diferentes (ex.: uma bate em cache, a
outra não) geram nós/arestas com `ExecutionCount`/`CallCount` menores que `TotalExecutions`; rota
sem nenhuma execução na janela devolve `TotalExecutions = 0` e as duas listas vazias; filtro
`from`/`to` aplicado sobre `Trace.StartedAt`.

### 16.2: `GetArchitectureMapAsync` em `NetApiFlow.Storage.PostgreSql` e `NetApiFlow.Storage.SqlServer`

Self-join de spans pelo `parent_span_id` dentro da mesma trace (`span s JOIN span p ON
s.parent_span_id = p.span_id AND s.trace_id = p.trace_id`), filtrado por `method`/
`route_template` da trace e pela janela, agregado em SQL ou em memória depois de trazer as linhas
(mesma escolha já feita em `GetComponentStatsAsync`/`GetRouteStatsAsync`: agregação em C# sobre
linhas cruas, não em SQL, para manter os três stores com a mesma lógica de agregação).

**Pronto quando:** `TraceStoreTestsBase` ganha os mesmos casos da tarefa 16.1 e passa contra
Postgres e SQL Server reais via Testcontainers, mesmo padrão de execução da Fase 6/14.

### 16.3: `GET /architecture`

`DashboardApiEndpoints` ganha a rota, `400` se faltar `method` ou `route` (mesma validação de
`/blueprint`).

**Pronto quando:** teste de integração confirma a forma da resposta contra um conjunto de traces
conhecido com pelo menos um caminho condicional (uma execução passa por um componente, outra
não), e que `400` dispara sem `method`/`route`.

### 16.4: Página `/architecture`

Nova feature `features/architecture/` (mesmo padrão de organização de `features/insights`), rota
`/architecture`, item novo na navegação. Seletor de rota (dropdown de `GET /endpoints`, mesmo
padrão de outras telas que já consomem essa lista) e seletor de janela (reaproveita
`shared/period-preset.ts` da Fase 14).

Novo construtor de grafo puro, `architecture-graph-builder.ts` (mesmo molde de
`graph-builder.ts`: sem import de Angular, sem `HttpClient`, testável sem `TestBed`), traduzindo
`ArchitectureMap` para `{ nodes: GraphNode[], edges: GraphEdge[] }` já no formato que
`flow-graph.component` consome. Campos sem equivalente no mapa agregado (`sequence`,
`durationMicros`, `nPlusOneCount`) vêm `null`. Percentual de ocorrência de um nó (`executionCount
/ totalExecutions`) embutido no texto de `operation` exibido quando menor que 100% (ex.:
`"TryGetAsync (14%)"`), sem precisar de campo novo em `GraphNode` nem mudança em
`flow-graph.component`.

Dois botões de export, os dois client-side, sem chamada de rede nova:
- **Mermaid**: função pura (`architectureMapToMermaid(map): string`) gerando um `flowchart TD`
  com nó e aresta rotulados, incluindo o percentual. Copia para a área de transferência ou baixa
  como `.mmd`.
- **SVG**: serializa o `<svg>` já renderizado pelo `flow-graph.component` (via
  `ElementRef`/`outerHTML`, com o namespace XML garantido) para um arquivo `.svg` baixável.

Estados de carregando, vazio (rota sem execução na janela) e erro tratados explicitamente.

**Pronto quando:** teste do construtor de grafo cobre a tradução de um `ArchitectureMap` com
caminho condicional para `GraphNode[]`/`GraphEdge[]` corretos, incluindo o percentual embutido no
texto. Teste do componente cobre o estado vazio e o refetch ao trocar rota/janela. Export de
Mermaid testado por snapshot de texto; export de SVG testado confirmando que o download dispara
com o tipo MIME certo (`URL.createObjectURL` mockado). `npx impeccable detect` rodado contra
`/architecture` nos dois temas e nos dois viewports do padrão
(`.claude/rules/design-quality.md`), contagem de avisos antes/depois no resumo da fase.

### 16.5: Documentação, CHANGELOG e a interação com tail sampling (R-19)

README e README.pt-BR.md ganham uma seção curta sobre `/architecture`: o que é, como ler o
percentual de ocorrência, e um aviso explícito (R-19) de que o mapa reflete o que está persistido
no `ITraceStore`, então uma amostragem tail-based agressiva (Fase 15) distorce o mapa para o lado
de erro/lento. `CHANGELOG.md`, seção `[Unreleased]` → `### Added`.

**Pronto quando:** README e README.pt-BR.md atualizados, conteúdo equivalente, aviso do R-19
visível na mesma seção. `CHANGELOG.md` atualizado. Teste de integração provando R-19 (`16.2`/
`16.3` com `TailSampling` ligado e `SuccessSampleRate` baixo, confirmando que o mapa reflete só o
que foi persistido, documentando a consequência no nome do teste).

## Aceite da fase

Todos os critérios de pronto acima. `dotnet build`/`test`/`format --verify-no-changes` limpos nos
dois TFMs. `npm run lint`/`format:check`/`test:ci`/`build` limpos. Nenhuma mudança em
`flow-graph.component` (renderer reaproveitado como está). Nenhuma mudança em `NetApiFlow.Mcp` ou
`NetApiFlow.OpenTelemetry`. R-19 provado, não corrigido (é a composição esperada de duas features
independentes).

## Ao terminar

Resumo para a Planejadora: o que foi feito, como verificar (comando exato, incluindo como gerar
execuções com pelo menos um caminho condicional de uma mesma rota para ver o percentual menor que
100% na prática, e como testar os dois exports), contagem de avisos do Impeccable antes/depois, o
que ficou pendente. A Planejadora roda `/revisar-fase 16` antes de decidir a próxima fatia do
roadmap (`docs/plano/roadmap-valor.md`).
