# Fase 15: Sampling tail-based

Projetos tocados: `src/NetApiFlow` (núcleo, `PersistenceWriter`/`NetApiFlowOptions`),
`src/NetApiFlow.Dashboard` (`GET /config`, `/history`).

Pré-requisito: nenhum funcional. Item 2 do roadmap D-58, topo do ranking de valor de entrega
depois da Fase 14 (D-82).

Contrato congelado em `docs/plano/contratos.md`: `TailSamplingOptions`,
`NetApiFlowOptions.TailSampling`, campos novos de `GET /config`. Decisão de onde a amostragem
decide (no momento da gravação, não numa limpeza posterior) travada em D-82, não renegociável
dentro desta fase.

## Contexto (ler antes de implementar)

Hoje `PersistenceWriter.Enqueue` (`src/NetApiFlow/Storage/PersistenceWriter.cs`) grava **toda**
trace completa no `ITraceStore` configurado, sem nenhum critério de relevância.
`ApplyRetentionAsync` só limpa depois, por quantidade (`Retention.MaxTraces`) ou idade
(`Retention.MaxAge`), nunca por "essa trace era chata". Em alto volume, isso enche o storage de
trace de sucesso e rápida, a imensa maioria do tráfego normal, que raramente interessa olhar
depois.

**Regra de amostragem (D-82):** com `TailSampling.Enabled = true`:
- Trace `Error`: sempre persistida.
- Trace com `DurationMicros >= SlowThreshold` (o mesmo `NetApiFlowOptions.SlowThreshold` que já
  existe, exposto via `GET /config` desde a Fase 4, D-24, até agora só usado para colorir a UI):
  sempre persistida.
- Qualquer outra trace (sucesso, não lenta): persistida com probabilidade
  `TailSampling.SuccessSampleRate` (padrão `0.1`, ou seja, 10%).

Com `TailSampling.Enabled = false` (padrão), nada muda: toda trace completa é persistida, como
hoje.

A decisão é tomada **no momento em que a trace está pronta para persistência** (status e duração
já conhecidos), antes de entrar na fila do `PersistenceWriter`. Não é uma segunda limpeza como
`ApplyRetentionAsync`: uma trace sampled-out nunca chega a ser gravada, não é gravada e depois
apagada.

**O que não muda:** a trace sampled-out continua aparecendo ao vivo, via SSE (`TraceStarted`,
`SpanStarted`, ..., `TraceCompleted`) e em `GET /traces/live`, porque os dois vêm direto do
`FlowRecorder` em memória, nunca do `ITraceStore`. Amostragem tail-based só decide o que entra no
**histórico persistido**, nunca o que aparece ao vivo.

## Fora de escopo, de propósito (D-82)

- Nenhuma mudança em `ITraceStore`: a decisão vive inteiramente em `PersistenceWriter`, as três
  implementações de store (`InMemoryTraceStore`, Postgres, SQL Server) nunca sabem que uma trace
  foi descartada antes de chegar até elas.
- Nenhum limiar novo de "lento": reaproveita `SlowThreshold` que já existe, não cria um segundo
  conceito para a mesma ideia.
- Sem amostragem configurável por rota ou por tenant nesta fase: um `SuccessSampleRate` global.
  Extensão natural se pedida depois, fora de escopo agora.

## Tarefas

### 15.1: `TailSamplingOptions` e validação

Novo `src/NetApiFlow/TailSamplingOptions.cs`, forma congelada em `contratos.md`.
`NetApiFlowOptions` ganha `TailSamplingOptions TailSampling { get; } = new();`.
`NetApiFlowOptionsValidator` ganha a checagem de `SuccessSampleRate` entre `0.0` e `1.0`
(inclusive), mesmo padrão de mensagem de erro já usado para `Retention`/`Limits`/`SlowThreshold`.

**Pronto quando:** teste cobre `SuccessSampleRate` fora do intervalo (negativo e acima de `1.0`)
falhando a validação, com a mensagem citando o valor recebido.

### 15.2: Função pura de decisão

Nova classe interna, ex. `TailSampleDecision`, com um método estático que recebe a trace já
completa (status, `DurationMicros`), as opções e **o sorteio como parâmetro** (`double` entre
`0.0` e `1.0`, não um `Random` interno): `ShouldPersist(FlowTrace trace, TailSamplingOptions
options, TimeSpan slowThreshold, double randomDraw) : bool`. Testável sem estatística instável,
mesmo padrão de função pura já usado em `computeUnexplainedTime`/`mergeIntervals` (Fase 12) e
`graph-builder` (Fase 2).

**Pronto quando:** teste unitário cobre: `Enabled = false` sempre devolve `true` (comportamento
atual preservado); trace `Error` sempre `true`; trace no limiar exato de `SlowThreshold` sempre
`true` (`>=`, não `>`); trace de sucesso rápida com `randomDraw` abaixo de `SuccessSampleRate`
devolve `true`, acima devolve `false`; `SuccessSampleRate = 0` e `SuccessSampleRate = 1` nos dois
extremos.

### 15.3: `PersistenceWriter` usa a decisão

`PersistenceWriter` ganha `IOptions<NetApiFlowOptions>` no construtor. `Enqueue` consulta
`TailSampleDecision.ShouldPersist(...)` (com `Random.Shared.NextDouble()` como sorteio real)
antes de escrever no canal; trace sampled-out nunca entra na fila, nunca chama
`ITraceStore.SaveAsync`. Novo contador `FlowMetrics.SampledOutTraces`
(`netapiflow.traces.sampled_out`), incrementado só quando uma trace é descartada por amostragem
(nunca pelo mesmo motivo de `DroppedTraces`, que é sobre limite de trace ativa em memória, outro
descarte).

**Pronto quando:** teste de integração com `TailSampling.Enabled = true` e
`SuccessSampleRate = 0` confirma que uma trace `Error` e uma trace acima de `SlowThreshold`
aparecem em `GET /traces`, mas uma trace de sucesso rápida gerada no mesmo teste não aparece (prova
R-18, documentando a consequência, não escondendo). Teste separado confirma que a mesma trace
sampled-out aparece normalmente via `GET /traces/live` ou num evento SSE capturado durante o
teste (prova de que o ao vivo não muda).

### 15.4: `GET /config` e aviso em `/history`

`DashboardConfig` ganha `TailSamplingEnabled`/`SuccessSampleRate`, espelhando
`NetApiFlowOptions.TailSampling`. `history.component.ts` (já consome `GET /config` para
`SlowThreshold`) mostra uma faixa curta quando `tailSamplingEnabled` é `true`, citando o
`successSampleRate` (ex. "Amostragem tail-based ativa: erros e rotas lentas sempre aparecem,
10% das demais execuções são guardadas"), i18n pt-BR/en.

**Pronto quando:** teste do componente confirma que a faixa aparece com `tailSamplingEnabled:
true` e não aparece com `false`. `npx impeccable detect` rodado contra `/history` nos dois temas
(contagem de avisos antes/depois no resumo da fase, mesma régua de
`.claude/rules/design-quality.md`).

### 15.5: Documentação e CHANGELOG

README e README.pt-BR.md ganham uma seção curta sobre `TailSampling`, explicando a regra de
"sempre erro, sempre lento, amostra do resto" e a consequência do R-18 (blueprint e métricas
agregadas passam a refletir a amostra, não o tráfego total, quando ligado). `CHANGELOG.md`, seção
`[Unreleased]` → `### Added`.

**Pronto quando:** README e README.pt-BR.md atualizados, conteúdo equivalente. `CHANGELOG.md`
atualizado.

## Aceite da fase

Todos os critérios de pronto acima. `dotnet build`/`test`/`format --verify-no-changes` limpos nos
dois TFMs. `npm run lint`/`format:check`/`test:ci`/`build` limpos. R-18 provado, não corrigido
(é o comportamento pretendido). Nenhuma mudança em `ITraceStore`, `NetApiFlow.Mcp` ou
`NetApiFlow.OpenTelemetry`.

## Ao terminar

Resumo para a Planejadora: o que foi feito, como verificar (comando exato, incluindo como gerar
traces suficientes para ver o descarte por amostragem na prática, com `SuccessSampleRate` baixo o
bastante para ser observável num teste manual curto), contagem de avisos do Impeccable
antes/depois, o que ficou pendente. A Planejadora roda `/revisar-fase 15` antes de decidir a
próxima fatia do roadmap (`docs/plano/roadmap-valor.md`).
