# Fase 18: Três correções em aberto (R-17, D-54, R-18/R-19)

Projetos tocados: `src/NetApiFlow` (três `ITraceStore`), `tests/NetApiFlow.IntegrationTests`,
`src/NetApiFlow.Dashboard/ClientApp` (página `/insights` e `/architecture`).

Pré-requisito: nenhum funcional. Mudança de direção pedida pelo usuário (D-88): em vez de seguir
o ranking de valor de entrega (`docs/plano/roadmap-valor.md`), esta fase ataca três riscos/dívidas
técnicas já registrados e nunca corrigidos. Nenhuma mudança de contrato público, nenhum modelo de
domínio novo: as três tarefas são bug fix, investigação de teste, e consistência de UI.

## Tarefa 18.1: R-17, case-sensitivity em `GetRouteStatsAsync`

Hoje `GetRouteStatsAsync` agrupa por `(Method, RouteTemplate)` com igualdade exata nos três
`ITraceStore` (`InMemoryTraceStore`, Postgres, SQL Server), enquanto `QueryAsync` já compara
`Method` com `StringComparison.OrdinalIgnoreCase` de propósito. Normalizar `Method` para
maiúsculo (`ToUpperInvariant()`) **na chave de agrupamento**, nos três stores, sem mudar o que
fica gravado em `FlowTrace.Method` (isso continua como o cliente mandou, só a chave de
agrupamento desta consulta normaliza). Campo `method` da resposta de `GET /metrics/routes`
também sai sempre maiúsculo, consistente com o resto da API.

**Pronto quando:** teste cobrindo o mesmo método em capitalizações diferentes (`GET` e `get`)
gerando uma única entrada de `RouteStat`, com `ExecutionCount` somado, nos três `ITraceStore`
(`TraceStoreTestsBase`, provado contra Postgres e SQL Server reais via Testcontainers, mesmo
padrão da Fase 14). Mitiga R-17.

## Tarefa 18.2: D-54, investigar o teste instável

`NetApiFlow.IntegrationTests.TracingIntegrationTests.Request_WithTaskWhenAll_ParallelSpansHaveCorrectParent`
(`net10.0`) já falhou sem relação com código em pelo menos uma PR anterior, passando na segunda
tentativa, nunca investigado a fundo. **Investigar antes de corrigir**: rodar o teste repetidas
vezes localmente (`dotnet test --filter`, um loop de N execuções) para tentar reproduzir a falha
de propósito, não só confiar na memória do que aconteceu antes. Hipótese registrada em D-54 é
sensibilidade a timing/agendamento do thread pool dentro do `Task.WhenAll`; confirmar ou refutar
antes de mexer no teste.

Se a causa for timing real (ex. uma asserção que depende de ordem que `Task.WhenAll` não
garante), a correção é no teste (tornar a asserção robusta à ordem real, não um `Thread.Sleep`
nem aumentar timeout às cegas). Se a causa for um bug real de produção (hierarquia de span errada
sob paralelismo), é achado novo: parar e relatar à Planejadora antes de decidir a correção, não
assumir que é só flakiness de teste.

**Pronto quando:** causa raiz identificada e documentada no resumo da fase (reproduzida ou não),
correção aplicada condizente com a causa, teste rodado em loop (mínimo 20 execuções) sem falha
antes de considerar resolvido. Se a causa raiz não for encontrada com certeza, registrar o que foi
tentado e o estado em que fica (não fechar D-54 como resolvido sem certeza real).

## Tarefa 18.3: R-18/R-19, aviso de tail sampling em `/insights` e `/architecture`

Hoje só `/history` (Fase 15) mostra a faixa avisando que `TailSampling` está ligado. `/insights`
(Fase 14, R-18) e `/architecture` (Fase 16, R-19) também leem do `ITraceStore` e sofrem a mesma
amostragem, mas não avisam quem está olhando. Reaproveitar a mesma faixa (`.naf-banner`,
`GET /config` já expõe `tailSamplingEnabled`/`successSampleRate`, mesmo texto i18n já existente
em `history.tailSampling.banner`, só reaproveitar a chave) nas duas páginas, mesmo critério de
visibilidade (`tailSamplingEnabled === true`).

**Pronto quando:** teste de componente em `InsightsComponent` e `ArchitectureComponent` confirma
que a faixa aparece quando `tailSamplingEnabled: true` e não aparece quando `false`, mesmo padrão
de teste já usado em `history.component.spec.ts`. `npx impeccable detect` rodado contra as duas
páginas com a faixa visível, nos dois temas, contagem de avisos antes/depois no resumo da fase.

## Fora de escopo, de propósito (D-88)

- R-06 (Npgsql interop, dedup entre `NetApiFlow.EntityFrameworkCore` e a `Activity` nativa do
  Npgsql): maior, exige investigação própria sobre como as duas `Activity` se relacionam. Fica
  para a Fase 19, planejada separadamente.
- D-61 (entry points não-HTTP): decisão de escopo/identidade do produto, não dívida técnica. Fica
  em aberto, sem decisão, até o usuário quiser essa conversa.
- Nenhuma mudança em `ITraceStore` além da normalização da tarefa 18.1 (sem método novo, sem
  campo novo).

## Aceite da fase

Todos os critérios de pronto acima. `dotnet build`/`test`/`format --verify-no-changes` limpos nos
dois TFMs. `npm run lint`/`format:check`/`test:ci`/`build` limpos. R-17 mitigado e provado. D-54
com causa raiz documentada (encontrada ou não, mas investigada de verdade). R-18/R-19 com o aviso
de amostragem consistente nas três páginas que leem do `ITraceStore` amostrado.

## Ao terminar

Resumo para a Planejadora: o que foi feito, como verificar (comando exato para cada uma das três
tarefas), o resultado real da investigação do D-54 (não só "corrigido", mostrar a evidência),
contagem de avisos do Impeccable antes/depois, o que ficou pendente. A Planejadora roda
`/revisar-fase 18` antes de decidir a Fase 19 (R-06, Npgsql).
