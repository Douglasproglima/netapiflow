# Fase 14: Visão agregada ("Insights"), estilo Pulse

Projetos tocados: `src/NetApiFlow` (núcleo, `ITraceStore`), `src/NetApiFlow.Storage.PostgreSql`,
`src/NetApiFlow.Storage.SqlServer`, `src/NetApiFlow.Dashboard` (API REST) e
`src/NetApiFlow.Dashboard/ClientApp` (página nova).

Pré-requisito: Fase 3 aceita (`ITraceStore`, `/metrics/components`, padrão a seguir). Item 1 do
roadmap D-60, topo do ranking de valor de entrega depois da Fase 13 (D-81).

Contrato congelado em `docs/plano/contratos.md`: `RouteStat`,
`ITraceStore.GetRouteStatsAsync`, `GET /metrics/routes`. Decisões de escopo e nome da página
travadas em D-81, não renegociáveis dentro desta fase.

## Contexto (ler antes de implementar)

D-60 pediu três coisas, nenhuma nova no modelo de dado, só um agregado diferente do que já
existe: rotas mais lentas, top erros, rotas mortas. O dado já existe em `FlowTrace`
(`Method`, `RouteTemplate`, `Status`, `DurationMicros`) e em `GET /endpoints` (toda rota
rastreável da API hospedeira, executada ou não). Falta só agrupar por rota em vez de por
componente, o que `GetComponentStatsAsync` já faz, só que por `(Component, Operation)` do span,
não por `(Method, RouteTemplate)` do trace.

**Nome da página é "Insights", não "Pulse".** "Pulse" é a referência de estilo usada no
planejamento (D-60, D-79, D-81) para descrever a forma da feature ("visão agregada, estilo
Pulse"), não um nome a copiar. Nome de produto de terceiro não vira nome de feature do NetApiFlow
(mesma régua de identidade do projeto que já vale para o `apiflow`, ver `CLAUDE.md`).

## Fora de escopo, de propósito (D-81)

- Nenhuma tool nova em `NetApiFlow.Mcp`. Extensão natural se pedida depois (D-59, uma feature por
  vez); `GetComponentStatsTool` já existe como precedente de como ficaria, não precisa ser
  replicado agora.
- Nenhuma métrica de infraestrutura (fila do `ThreadPool`, CPU, GC): mesma fronteira já travada
  na Fase 12, cruzaria o não-objetivo "no infrastructure metrics" do README.
- Sem alerta/notificação (e-mail, webhook, push) sobre rota lenta ou com erro: é visão, não
  monitoramento ativo. Cruzaria o não-objetivo "no alerting" do README se existisse.

## Tarefas

### 14.1: `RouteStat` e `GetRouteStatsAsync` no núcleo

Novo `src/NetApiFlow/RouteStat.cs`, forma congelada em `contratos.md`. `ITraceStore` ganha
`GetRouteStatsAsync(StatsQuery query, CancellationToken cancellationToken)`.
`InMemoryTraceStore` implementa agrupando `_entries.Values` por
`(Trace.Method, Trace.RouteTemplate)`, mesmo cálculo de mediana/média/p95/máximo já usado em
`GetComponentStatsAsync`/`GetTraceStatsAsync` (reaproveitar o helper `Percentile` existente, não
duplicar).

**Pronto quando:** teste unitário cobre: múltiplas rotas distintas agregadas separadamente;
mesma rota com métodos HTTP diferentes (`GET /api/orders/{id}` e `DELETE /api/orders/{id}`)
tratada como duas entradas; filtro `from`/`to` aplicado sobre `Trace.StartedAt`; trace `Running`
(duration `null`) não quebra o cálculo de p95/média (mesmo tratamento de
`GetTraceStatsAsync`: duração nula fica fora do cálculo de duração, mas conta em
`ExecutionCount`).

### 14.2: `GetRouteStatsAsync` em `NetApiFlow.Storage.PostgreSql` e `NetApiFlow.Storage.SqlServer`

Mesma query SQL agregada já usada para `GetComponentStatsAsync` nos dois storages, trocando a
fonte (tabela de traces, não de spans) e a chave de agrupamento (`method`, `route_template`).

**Pronto quando:** `TraceStoreTestsBase` (base de teste compartilhada pelos três `ITraceStore`)
ganha os casos de `GetRouteStatsAsync` e passa contra Postgres e SQL Server reais via
Testcontainers, mesmo padrão de execução da Fase 6.

### 14.3: `GET /metrics/routes`

`DashboardApiEndpoints` ganha a rota, mesmo padrão de parsing de `from`/`to` já usado em
`GET /metrics/components`.

**Pronto quando:** teste de integração confirma a forma da resposta contra um conjunto de traces
conhecido, incluindo pelo menos duas rotas e um filtro de `from`/`to` que exclui uma delas.

### 14.4: Página "Insights" no dashboard

Nova feature `features/insights/` (mesmo padrão de organização de `features/components`), rota
`/insights`, item novo na navegação principal. Três blocos:

- **Rotas mais lentas**: top N (sugestão: 10) por `p95DurationMicros` decrescente, do payload de
  `GET /metrics/routes`.
- **Top erros**: top N por `errorCount` decrescente, do mesmo payload (uma segunda ordenação, não
  uma segunda chamada de API).
- **Rotas mortas**: toda rota de `GET /endpoints` sem nenhuma entrada correspondente
  (`method`+`routeTemplate`) em `GET /metrics/routes` da janela escolhida.

Janela de tempo com um seletor simples (não um date range picker completo): últimas 24 horas,
últimos 7 dias (padrão, conforme D-60), últimos 30 dias. Troca de janela refaz as duas chamadas
(`/metrics/routes`, `/endpoints`) com `from` calculado no frontend.

Estados de carregando, vazio (nenhuma rota mortas é "vazio" legítimo, não erro) e erro tratados
explicitamente, mesma régua de `.claude/rules/frontend.md`.

**Pronto quando:** teste de componente cobre os três blocos com dado conhecido (incluindo o caso
de nenhuma rota morta, que não é a mesma coisa que erro de carregamento), e o seletor de janela
refaz a consulta com o `from` esperado para cada opção. `npx impeccable detect` rodado contra
`/insights` nos dois temas e nos dois viewports do padrão (`.claude/rules/design-quality.md`),
contagem de avisos antes/depois registrada no resumo da fase.

### 14.5: Non-goals do README (pendência explícita do D-60)

Revisar a frase "not an APM... no dashboards beyond the one it ships" nos dois READMEs à luz
desta fase. `/insights` é uma página a mais dentro do mesmo pacote `NetApiFlow.Dashboard` que já
existe, não um segundo dashboard nem um produto novo: decisão default é manter a frase como está
e documentar essa leitura no resumo da fase. Se a Executora achar um motivo técnico concreto para
a frase precisar mudar mesmo assim, parar e relatar à Planejadora antes de editar, não decidir
sozinha (é mudança de non-goals, afeta expectativa de quem lê o README).

**Pronto quando:** decisão registrada no resumo da fase (mudou ou não mudou, e por quê), README e
README.pt-BR.md consistentes entre si em qualquer dos dois casos.

### 14.6: Documentação e CHANGELOG

README e README.pt-BR.md ganham uma seção curta sobre `/insights`, mesmo padrão das seções já
existentes (N+1, tempo não explicado, OTLP). `CHANGELOG.md`, seção `[Unreleased]` → `### Added`.

**Pronto quando:** README e README.pt-BR.md atualizados, conteúdo equivalente. `CHANGELOG.md`
atualizado.

## Aceite da fase

Todos os critérios de pronto acima. `dotnet build`/`test`/`format --verify-no-changes` limpos nos
dois TFMs. `npm run lint`/`format:check`/`test:ci`/`build` limpos. Nenhuma mudança em
`NetApiFlow.Mcp`. Avisos do Impeccable antes/depois registrados no resumo, sem regressão em
`low-contrast`/`undersized-ui-text` (bloqueantes, `.claude/rules/design-quality.md`).

## Ao terminar

Resumo para a Planejadora: o que foi feito, como verificar (comando exato, incluindo como gerar
traces suficientes para ver os três blocos populados e pelo menos uma rota morta de verdade), a
decisão tomada na tarefa 14.5 (non-goals do README) e por quê, contagem de avisos do Impeccable
antes/depois, o que ficou pendente. A Planejadora roda `/revisar-fase 14` antes de decidir a
próxima fatia do roadmap (`docs/plano/roadmap-valor.md`).
