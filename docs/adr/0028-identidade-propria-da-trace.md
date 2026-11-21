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
