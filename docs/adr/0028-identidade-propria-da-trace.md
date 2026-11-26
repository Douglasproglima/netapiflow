# ADR-0028: a trace do NetApiFlow tem identidade própria, e o W3C trace id vira uma tag

## Status
Aceito

## Discussion
[#383](https://github.com/Douglasproglima/netapiflow/discussions/383)

## Contexto

O NetApiFlow usa o **W3C trace id** da requisição como identidade da trace. É o `activity.TraceId` da `Activity`
que o ASP.NET Core abre para a requisição, e quando a requisição traz um `traceparent` o runtime o herda. Mas um
W3C trace id existe para ligar **várias** requisições de uma mesma operação distribuída, e o NetApiFlow modela uma
trace por **requisição**. As duas coisas não são a mesma. Quem chama a API duas vezes pelo `HttpClient` (que propaga o
`traceparent` por padrão) manda o mesmo trace id nas duas, e é o caso padrão de microsserviços em .NET.

A auditoria de base achou o problema (S3), e a Planejadora o mediu nos dois bancos. O id é a chave de tudo: do
recorder, das stores, do cabeçalho `X-NetApiFlow-TraceId`, do REST, do SSE e das ferramentas do MCP. O cliente
escolhe essa chave.

Opções:

| Opção | Resultado |
|---|---|
| **A. Id próprio por requisição, aleatório e gerado pelo servidor. O W3C trace id vira a tag `w3c.trace_id` da trace** | **Escolhida** |
| B. Manter o W3C trace id como chave e fazer a store gravar por `upsert` ou por acréscimo | Descartada: não resolve a mistura de etapas de requisições concorrentes nem o cliente escolher a chave, e o `upsert` trocaria o "o primeiro vence" dos bancos por "o último vence" sem ninguém pedir |
| C. Manter o W3C trace id e acrescentar um sufixo quando houver colisão | Descartada: a colisão só é vista com a trace ativa ou já gravada, e o sufixo dependeria de uma consulta à store a cada requisição |
| D. Compor o id como o W3C trace id mais o `SpanId` da atividade do servidor | Descartada: o id passa a ter 49 caracteres e muda o formato em todo lugar que o mostra, e ainda exige o mesmo caminho pela cadeia de pais para achar a trace de uma etapa |

## Evidência

**Código** (`develop`, lido pela Planejadora):

- `NetApiFlowMiddleware`: `traceId = activity.TraceId.ToString()`, com `context.TraceIdentifier` só quando não há
  `Activity`.
- `FlowRecorder.TryStartTrace`: `_activeTraces.TryAdd(traceId, ...)` devolve `false` se já há uma trace ativa com o
  mesmo id, e então a requisição não é rastreada.
- `FlowActivityListener` (linhas 84 e 162): toda etapa acha a trace por `activity.TraceId`.
- `InMemoryTraceStore`: `_entries[trace.TraceId] = new Entry(...)`, que sobrescreve.
- Os dois `schema.sql`: `trace_id varchar(64) PRIMARY KEY`, e a gravação é um `COPY` (Postgres) e um `SqlBulkCopy`
  (SQL Server).
- `NetApiFlow.CurrentTraceId`, API pública: `Activity.Current?.TraceId.ToString()`, ou seja, hoje é o W3C trace id.

**Medição da Planejadora**, contra os pacotes publicados `0.5.2`, `net10.0`, com PostgreSQL 16 e SQL Server 2022
em contêiner, três requisições seguidas com o **mesmo** `traceparent` (`/orders`, `/stock`, `/orders`), e duas
requisições **ao mesmo tempo** com outro `traceparent` a uma rota de 1,5 s com uma etapa dentro:

| | PostgreSQL | SQL Server |
|---|---|---|
| Em sequência, as três dão | 200 | 200 |
| Linhas guardadas para o id | **uma**, a primeira (`/orders`) | **uma**, a primeira (`/orders`) |
| As outras duas | Falham com "failed to persist trace", com exceção (14 linhas de log) | Falham com `Violation of PRIMARY KEY constraint ... Cannot insert duplicate key in object 'dbo.netapiflow_traces'` (10 linhas de log) |
| Ao mesmo tempo, a segunda requisição | **Não recebe** `X-NetApiFlow-TraceId` (não foi rastreada) | **Não recebe** o cabeçalho |
| A trace guardada | `span_count` 4: dois `Endpoint /slow` e dois `Work.Slow` | `span_count` 4, os mesmos |

Ou seja: nos bancos o primeiro vence e o resto vira erro de log, e na store em memória o **último** vence, pela
medição da auditoria (só `/stock` fica). E, com duas requisições ao mesmo tempo, as etapas de **as duas** acabam
numa trace só. A auditoria mediu também que qualquer cliente anônimo escolhe o id e sobrescreve uma trace guardada
na store em memória.

## Decisão

Opção A, com quatro emendas da auditoria (20/11/2026).

- O `NetApiFlowMiddleware` gera, no início de cada requisição rastreada, um id de 128 bits aleatório, do gerador do
  runtime, em 32 caracteres hexadecimais, que é o **formato de hoje**. É a `FlowTrace.TraceId`: a chave do recorder
  e das stores, o valor de `X-NetApiFlow-TraceId`, o que o REST, o SSE e o MCP mostram. Nada disso muda de formato.
- **Como a etapa acha a trace: a Tarefa 55.0 mede duas formas e escolhe.** (1) Subir a cadeia de `Activity.Parent`
  até a atividade do servidor, onde o middleware gravou o id como propriedade customizada (`SetCustomProperty`). É o
  caminho que a hierarquia de spans já segue (`.claude/rules/tracing.md`), mas uma atividade criada com um
  `ActivityContext` explícito como pai remoto quebra a subida. (2) Um `AsyncLocal` com o id, gravado pelo
  middleware. Ele flui junto com o `ExecutionContext`, do mesmo jeito que o `Activity.Current`, é O(1), não depende de
  a cadeia de pais estar inteira, e um pai remoto explícito não o quebra. A escolhida é a que passar em **todos** os
  casos da 55.0 com o menor custo medido, e, se empatarem, a mais simples. Os quatro pontos que hoje leem
  `activity.TraceId` (o middleware e três no `FlowActivityListener`) passam por uma função só.
- O W3C trace id, quando a requisição tem `Activity`, vira a tag `w3c.trace_id` da trace. `FlowTrace` já tem tags,
  então **nenhum campo novo e nenhuma coluna nova**.
- **A volta da correlação com o OTLP.** O SDK exporta as etapas com o W3C trace id, então quem acha um span num
  backend como o Jaeger precisa chegar à trace do NetApiFlow. **Toda atividade do NetApiFlow ganha a tag
  `netapiflow.trace_id`**, e a trace guarda `w3c.trace_id`. Um teste prova que um span exportado carrega o trace id
  W3C nativo e a tag.
- `NetApiFlow.CurrentTraceId` passa a devolver o **id do NetApiFlow**, porque é a identidade que toda a API usa
  (`GetRecentTrace`, o cabeçalho, o REST, o MCP), e `null` quando não há trace da requisição em andamento. O W3C
  continua acessível pelo caminho padrão, `Activity.Current.TraceId`. É uma mudança de significado de API pública
  antes da `1.0`: entra em `### Changed` do `CHANGELOG.md`, com uma linha no XML doc e no README, escrita sem
  ambiguidade.
- **Os dois ids têm o mesmo formato, 32 caracteres hexadecimais, e isso engana o olho:** quem cola o id do painel
  no Jaeger não acha nada. Os dois aparecem **rotulados** no detalhe da trace ("Trace" e "W3C trace"), e o README
  explica a diferença numa frase. Buscar no painel pelo W3C trace id fica no roadmap, não nesta fase.
- As traces já gravadas ficam como estão: o id antigo é uma string opaca válida, e não há migração.

## Consequências

- Resolve o que foi medido: nenhuma colisão possível, nenhum erro de chave primária, duas requisições com o mesmo
  W3C trace id viram duas traces, e cada uma guarda só as etapas dela. E o cliente deixa de escolher a chave: ele
  controla só a tag `w3c.trace_id`, que o runtime só aceita como 32 caracteres hexadecimais.
- **Muda de significado** `NetApiFlow.CurrentTraceId` e o que `NetApiFlow.GetRecentTrace(services, traceId)` espera.
  Quem copia o W3C trace id de um log para a URL do painel não acha mais a trace pelo id: acha pela tag, e a busca por
  essa tag no painel fica fora (item de roadmap, se alguém pedir). Antes da `1.0`, mas entra em `### Changed` do
  `CHANGELOG.md` e no README, porque afeta quem instala.
- **Risco:** com a subida pelos pais, uma etapa cuja cadeia não chega à atividade do servidor deixaria de ser
  anexada à trace, e hoje é anexada pelo W3C trace id. O `AsyncLocal` não quebra num pai remoto explícito, mas tem o
  seu limite: um `ExecutionContext` suprimido. A Tarefa 55.0 mede os casos **antes** do código: `Task.Run`,
  `Task.WhenAll`, `IAsyncEnumerable`, `HttpClient` de saída, EF Core, SqlClient, Npgsql,
  `ExecutionContext.SuppressFlow` (que o README já diz perder a etapa) e uma atividade criada com um
  `ActivityContext` explícito. Se algum caso que funciona hoje deixar de funcionar com a forma escolhida, para e
  relata.
- **Custo:** o da forma escolhida, medido com `tests/NetApiFlow.Benchmarks` e `[MemoryDiagnoser]`, mais uma tag
  por atividade do NetApiFlow.
- **Dependência do ADR-0027 (`PathBase`):** o mecanismo escolhido lá adia a abertura da trace até o
  `EndpointMatched`. O id e a propriedade nascem quando a atividade do servidor é vista pela primeira vez, e o
  ADR-0027 decide o que acontece com as etapas anteriores à abertura. Os dois precisam ser coerentes antes de
  qualquer código de qualquer um.
- **OTLP:** os spans exportados continuam com o W3C trace id nativo, e cada atividade do NetApiFlow carrega
  `netapiflow.trace_id`, que liga o backend ao painel.

## Adendo de 21/11/2026: o que a Tarefa 55.0 mediu e a forma escolhida

A decisão acima não muda. O adendo fecha o que ela deixou para a medição, e corrige uma contagem. Quem mediu foi a Executora, em host real, `net10.0`, e a Planejadora leu o relatório e não repetiu as medições.

**São seis os pontos que leem o trace id da `Activity`,** não quatro nem cinco: o middleware, o `Sample` do `FlowActivityListener` (que só tem `options.TraceId` e o `Activity.Current` ambiente, nunca a atividade nova), o `OnActivityStarted`, o `OnActivityStopped`, o `NetApiFlow.CurrentTraceId` e o `FlowLogger`, que correlaciona o log à trace e o ADR não listava. Os dois usos de `CurrentTraceId` no `FlowAuthorizationFilterListener` ficam coerentes sem mudança. **Os seis passam por uma função só.**

**A matriz,** medida contra o que anexa a etapa hoje, contra a subida pela cadeia de `Activity.Parent` e contra o `AsyncLocal`:

| Caso | Hoje | Cadeia | `AsyncLocal` |
|---|---|---|---|
| `Task.Run`, `Task.WhenAll`, `IAsyncEnumerable`, `HttpClient` de saída, EF Core, SqlClient, Npgsql | sim | sim | sim |
| `ExecutionContext.SuppressFlow` | não | não | não |
| `ActivityContext` explícito, fluxo mantido | sim | **não** | sim |
| `ActivityContext` explícito, sem fluxo | sim | **não** | **não** |
| `Activity.Current` atribuído à mão numa thread sem fluxo | sim | sim | **não** |

Nenhuma das duas formas sozinha cobre tudo o que funciona hoje, e a união das duas cobre tudo, menos a quarta linha.

**A forma escolhida:** o middleware grava o estado da requisição (o id próprio e o W3C trace id) num `AsyncLocal`, e grava o id próprio como propriedade customizada da atividade do servidor. A função única resolve nesta ordem: (1) se há estado no `AsyncLocal` **e o W3C trace id da atividade é o dele**, o id próprio; (2) senão, sobe `Activity.Parent` até uma atividade que tenha a propriedade; (3) senão, nenhuma trace. No `Sample` a comparação usa `options.TraceId` e a subida parte de `Activity.Current`.

**Por que a comparação com o W3C trace id no passo 1, que é a Planejadora acrescentando ao que a medição propôs:** o `AsyncLocal` é ambiente. Um worker de vida longa, iniciado dentro de uma requisição A, guarda o `ExecutionContext` dela para sempre, e depois processa a atividade de uma requisição B. Sem a comparação, essa atividade seria pendurada em A enquanto A ainda está ativa, que é a mistura que a fase conserta. Com ela, o `AsyncLocal` só vale para uma atividade que pertence ao mesmo W3C trace id da requisição, que é exatamente a condição em que a etapa é anexada hoje. O custo é uma comparação de 16 bytes, e o estado é o `FlowRequestState` que a Fase 53 já cria por requisição, então não há alocação a mais.

**A perda aceita: o `ActivityContext` explícito sem fluxo.** Funciona hoje só porque o W3C trace id é compartilhado, que é o mecanismo do defeito. Exige suprimir o fluxo e ainda criar a atividade com o pai explícito, e o caso real que se imagina (um worker que consome uma fila em processo) só anexa hoje se a requisição ainda estiver ativa, ou seja, por acaso de tempo. É a mesma classe de perda do `SuppressFlow`, que o README já diz. **Não se monta um índice por W3C trace id**, porque ele traria de volta a ambiguidade que a fase conserta. Se um caso real aparecer, a opção é um índice que só anexa quando exatamente uma trace ativa tem aquele W3C trace id, e isso é uma decisão nova.

**Custo,** medido pela Executora com `BenchmarkDotNet` em `ShortRun`, ordem de grandeza: ler o `AsyncLocal` 10 ns e 0 B, subir a cadeia de 38 a 44 ns e 0 B (profundidade 1 a 6), gravar o `AsyncLocal` uma vez por requisição 9 ns, gravar a propriedade na atividade do servidor uma vez por requisição 39 ns, e a tag `netapiflow.trace_id` em cada atividade +31 ns e +80 B. Gravar uma propriedade em **toda** atividade custaria +140 ns e +216 B, e por isso não se faz. A tag chega ao span exportado pelo SDK, que mantém o W3C trace id nativo. O benchmark de ponta a ponta contra a `develop` é da Executora, depois da 55.1.

**Coerência com o ADR-0027:** o id nasce no início do middleware, junto da reserva não publicada que a Fase 53 já faz, e a trace abre no `EndpointMatched`. A etapa anterior à abertura acha a trace reservada pelo mesmo id, e o cabeçalho sai do `FlowRequestState`. Só o `StartRequest` muda.
