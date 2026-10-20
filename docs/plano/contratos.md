# Contratos

Definidos antes da implementação (seção 15.4 do `netapiflow-prompt.md`). A Executora não diverge
destes contratos sem avisar a Planejadora. Mudança aqui depois de uma fase começar é decisão da
Planejadora, não ajuste silencioso durante a implementação.

## 1. Núcleo: superfície pública

Tudo que não está nesta lista nasce `internal`. `FlowTrace` e `FlowSpan` ficam num namespace sem
nenhum `using` de ASP.NET, EF Core ou driver de banco (provado por teste de arquitetura, tarefa
1.1 em `fase-1-nucleo-de-captura.md`).

```csharp
namespace NetApiFlow;

public static class NetApiFlow
{
    /// <summary>Marks the start of a tracked step. Dispose the returned <see cref="FlowStep"/>
    /// to close it.</summary>
    public static FlowStep Step(string component, string operation,
                                 ComponentType type = ComponentType.Custom);

    /// <summary>Trace id of the request currently executing, or <c>null</c> when tracing is
    /// disabled or there is no ambient trace.</summary>
    public static string? CurrentTraceId { get; }
}

public readonly struct FlowStep : IDisposable
{
    public void Tag(string key, string value);
    public void MarkError(Exception exception);
    public void Dispose();
}

public enum ComponentType { Endpoint, Controller, UseCase, Service, Repository, Database, Http, Custom }
public enum FlowStatus { Running, Success, Error }

public interface IFlowEventListener
{
    ValueTask OnEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken);
}

public interface ITraceStore
{
    ValueTask SaveAsync(FlowTrace trace, IReadOnlyList<FlowSpan> spans, CancellationToken cancellationToken);
    ValueTask<FlowTraceDetail?> GetAsync(string traceId, CancellationToken cancellationToken);
    ValueTask<PagedResult<FlowTraceSummary>> QueryAsync(TraceQuery query, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<ComponentStat>> GetComponentStatsAsync(StatsQuery query, CancellationToken cancellationToken);
    ValueTask<TraceStats> GetTraceStatsAsync(StatsQuery query, CancellationToken cancellationToken);
    ValueTask<FlowTraceDetail?> GetLastSuccessfulAsync(string method, string routeTemplate, CancellationToken cancellationToken);
    ValueTask<int> ApplyRetentionAsync(RetentionPolicy policy, CancellationToken cancellationToken);
}

public interface IFlowTenantResolver { string? Resolve(HttpContext context); }
public interface IDashboardAuthorizationFilter { ValueTask<bool> AuthorizeAsync(HttpContext context); }

public sealed class NetApiFlowOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan SlowThreshold { get; set; } = TimeSpan.FromMilliseconds(250);
    public bool CaptureSqlText { get; set; }
    public RetentionOptions Retention { get; }
    public LimitsOptions Limits { get; }
    public IReadOnlyList<string> IncludePatterns { get; }
    public IReadOnlyList<string> ExcludePatterns { get; }
    public NetApiFlowOptions IncludePaths(params string[] patterns);
    public NetApiFlowOptions ExcludePaths(params string[] patterns);
}

public sealed class RetentionOptions
{
    public int MaxTraces { get; set; } = 1000;
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(24);
}

public sealed class LimitsOptions
{
    public int MaxSpansPerTrace { get; set; } = 300;
}

public static class NetApiFlowServiceCollectionExtensions
{
    public static IServiceCollection AddNetApiFlow(this IServiceCollection services,
                                                    Action<NetApiFlowOptions>? configure = null);
}

public static class FlowTrackedServiceCollectionExtensions
{
    public static IServiceCollection AddFlowTracked<TInterface, TImplementation>(
        this IServiceCollection services, ComponentType componentType,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TInterface : class where TImplementation : class, TInterface;

    public static IServiceCollection DecorateFlowTracked(
        this IServiceCollection services, Func<Type, bool> predicate,
        ComponentType componentType = ComponentType.Service);
}

public static class NetApiFlowEndpointFilterExtensions
{
    public static RouteHandlerBuilder WithNetApiFlowTracking(this RouteHandlerBuilder builder);
    public static RouteGroupBuilder WithNetApiFlowTracking(this RouteGroupBuilder builder);
}
```

`NetApiFlow.MediatR` expõe `AddNetApiFlowMediatR(this IServiceCollection)` (registra o
`IPipelineBehavior<,>` que gera span `UseCase`, além do `AddMediatR(...)` do próprio MediatR).
`NetApiFlow.EntityFrameworkCore` expõe `AddNetApiFlowEntityFrameworkCore(this IServiceCollection)`
(registra o interceptor; ainda precisa de `optionsBuilder.AddInterceptors(...)` no `DbContext`,
porque o EF Core não tem registro global de interceptor). Formalizado na revisão da Fase 2.

Formalizado na revisão da Fase 1 (estava ausente desta lista desde o bootstrap, embora a seção 9
do prompt já descrevesse essa forma de configuração em código).

### DTOs de apoio de `ITraceStore` (provisórios até a Fase 3)

`PagedResult<T>`, `FlowTraceSummary`, `TraceQuery`, `ComponentStat`, `StatsQuery` e
`RetentionPolicy` existem desde a Fase 1 com a forma implementada em `src/NetApiFlow/*.cs`,
cobrindo os filtros da seção 7.1 item 3 (status, método, rota, tenant, período, duração mínima,
paginação) e as métricas da seção 7.1 item 4 (execuções, erros, mediana, média, p95, máximo).
Cada um traz `/// <remarks>Provisional shape...</remarks>` no comentário XML. O contrato
definitivo (campo a campo, JSON de exemplo) continua sendo responsabilidade da tarefa 3.1 — a
Fase 3 pode renomear ou reformar esses tipos sem que isso seja uma quebra de contrato desta
seção, só uma substituição do que era provisório.

`GetTraceStatsAsync` e `TraceStats` foram acrescentados a `ITraceStore` durante a tarefa 3.2
(D-18 em `docs/plano/decisoes.md`): o contrato de `/metrics` precisa de agregado por trace
inteiro (total, erros, média, p95, máximo), que não existe em `GetComponentStatsAsync`
(agregado por componente/operação) nem dá para derivar dele de forma confiável. Mesmo padrão de
`ComponentStat`: contagem bruta, não taxa pronta.

```csharp
public sealed record TraceStats
{
    public required int TotalTraces { get; init; }
    public required int ErrorCount { get; init; }
    public required long AverageDurationMicros { get; init; }
    public required long P95DurationMicros { get; init; }
    public required long MaxDurationMicros { get; init; }
}
```

`errorRate` do payload de `/metrics` é calculado pela camada de API (`ErrorCount / TotalTraces`,
`0` quando `TotalTraces` é `0`), não pelo `ITraceStore`.

`GetAsync` e `GetLastSuccessfulAsync` passaram a devolver `FlowTraceDetail?` em vez de
`FlowTrace?` (D-19 em `docs/plano/decisoes.md`), achado durante a tarefa 3.2: os três
consumidores reais desses dois métodos (`GET /traces/{traceId}`, `GET
/traces/{traceId}/spans/{spanId}` e `GET /blueprint`) sempre precisam do trace **e** dos spans
juntos, nunca só do trace. Forçar uma segunda chamada ao store para buscar os spans seria uma
troca de rede a mais em todo acesso, permanente, contra os storages de banco da Fase 6.

```csharp
public sealed record FlowTraceDetail
{
    public required FlowTrace Trace { get; init; }
    public required IReadOnlyList<FlowSpan> Spans { get; init; }
}
```

`SaveAsync` continua recebendo trace e spans como parâmetros separados: é escrita, não leitura,
e os dois já chegam como peças distintas de quem monta a chamada (`PersistenceWriter`). Mudar
`SaveAsync` por simetria não tem benefício real, só mexeria em código que já funciona.

Notas de implementação, não normativas (a Executora decide o detalhe, mas não a forma pública
acima):

- `FlowStep` é `readonly struct` por decisão D-09: com tracing desligado, carrega `Activity? =
  null` e `Dispose` não faz nada. Zero alocação no caminho frio.
- `DispatchProxy` (`AddFlowTracked<TInterface, TImpl>`, `DecorateFlowTracked(predicate)`) e os
  filtros de Controller/Minimal API usam `NetApiFlow.Step(...)` por baixo. Não precisam de API
  pública própria além da extensão de `IServiceCollection` (seção 6.3 do prompt).
- `FlowTrace`/`FlowSpan` seguem o modelo de domínio da seção 6.4 do prompt, literalmente.

## 2. Evento SSE

Quadro na rede:

```
id: 1427
event: SpanCompleted
data: {"eventId":1427,"type":"SpanCompleted","traceId":"4bf92f3577b34da6a3ce929d0e0e4736","timestamp":"2026-10-19T13:42:11.123456Z","span":{...}}

```

Corpo do `data`, JSON camelCase:

```json
{
  "eventId": 1427,
  "type": "SpanCompleted",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "timestamp": "2026-10-19T13:42:11.123456Z",
  "span": {
    "spanId": "00f067aa0ba902b7",
    "parentSpanId": "0020000000000001",
    "sequence": 7,
    "component": "StockService",
    "operation": "ReserveAsync",
    "type": "Service",
    "startedAt": "2026-10-19T13:42:11.121624Z",
    "endedAt": "2026-10-19T13:42:11.123456Z",
    "durationMicros": 1832,
    "status": "Success",
    "errorType": null,
    "errorMessage": null,
    "isErrorOrigin": false,
    "tags": {}
  }
}
```

Eventos de `TraceStarted` e `TraceCompleted` trazem a chave `trace` (forma de `FlowTrace`) em vez
de `span`. Os seis tipos de evento são os da seção 6.5 do prompt: `TraceStarted`, `SpanStarted`,
`SpanCompleted`, `SpanFailed`, `TraceCompleted`, `TracePersisted`.

Regras do stream:

- `eventId` é sequencial por processo (não reinicia por trace) e é o valor usado como
  `Last-Event-ID` na reconexão.
- **`GET /stream` aceita o último id recebido de duas formas (D-22):** o header `Last-Event-ID`
  (reconexão automática do próprio navegador, que o `EventSource` nativo já faz sozinho) **ou**
  a query string `?lastEventId=<id>` (reconexão manual feita pelo código da aplicação, porque
  `EventSource` não permite setar headers numa conexão nova criada por código). Se os dois
  vierem, o header vale. Sem nenhum dos dois, começa do zero. Aditivo: não muda nada de quem já
  usava só o header.
- `event:` sempre presente, para o cliente filtrar sem desserializar o corpo.
- Heartbeat a cada 15 segundos, para o proxy intermediário não derrubar a conexão por
  inatividade. **Formato diverge por TFM (D-20):** em `net8.0` é um comentário SSE puro
  (`: ping`), fiel à letra deste contrato; em `net10.0`, `TypedResults.ServerSentEvents` só
  expõe `SseItem<T>` (sempre com `data:`), então o heartbeat ali é um evento sintético
  `event: ping` com `data: ""`. Efeito prático idêntico (mantém a conexão viva); o cliente deve
  ignorar qualquer evento `ping`, nunca depender do formato exato.
- **Ping de handshake (D-20, não previsto na versão original deste contrato):** a primeira coisa
  que o servidor escreve, antes de qualquer evento real ou do primeiro heartbeat, é um evento
  `event: ping` com `data: {}` e **sem `id:`**. Existe para forçar o flush dos headers HTTP assim
  que a conexão abre; sem ele, nada chega ao cliente até o primeiro evento real ou o primeiro
  heartbeat de 15s, o que inclui os próprios headers da resposta. O cliente deve tratá-lo como
  qualquer evento `ping`: ignorar.
- Buffer de replay circular de 1000 eventos (seção 6.7 da tabela de limites): reconexão com
  `Last-Event-ID` dentro da janela recebe o que perdeu; fora da janela, recebe só os eventos
  novos a partir da reconexão (sem erro, sem garantia de completude total, documentado como
  limitação conhecida).

## 3. API REST

Base configurável, padrão `/flow/api`. Só DTO, JSON camelCase. Todo campo de duração é
`*Micros` (`long`, microssegundos). Todo timestamp é UTC, formato ISO 8601
(`yyyy-MM-ddTHH:mm:ss.ffffffZ`). Parâmetro de query ausente = sem filtro naquele campo.
Erro de validação de entrada (ex.: `page < 1`) responde `400` com
`application/problem+json` (`ProblemDetails` padrão do ASP.NET Core), sem corpo customizado.

As rotas do próprio NetApiFlow nunca são rastreadas (nunca aparecem como span `Endpoint` ou
`Controller` no próprio dashboard). Congelado na tarefa 3.1: a partir daqui, mudança é decisão
da Planejadora, igual a qualquer outro contrato deste arquivo.

### GET `/traces`

Lista paginada com filtros, mapeados de `TraceQuery` (`src/NetApiFlow/TraceQuery.cs`).

Query string:

| Parâmetro | Tipo | Obrigatório | Filtra |
|---|---|---|---|
| `status` | `Running` \| `Success` \| `Error` | não | `FlowTrace.Status` |
| `method` | string | não | `FlowTrace.Method`, exato |
| `route` | string | não | `FlowTrace.RouteTemplate`, exato (não é glob) |
| `tenant` | string | não | tag `tenant` do trace (seção 6.9 do prompt) |
| `from` | ISO 8601 | não | `StartedAt >= from` |
| `to` | ISO 8601 | não | `StartedAt <= to` |
| `minDurationMicros` | long | não | `DurationMicros >= minDurationMicros` |
| `page` | int, padrão `1` | não | 1-based |
| `pageSize` | int, padrão `50`, máximo `200` | não | acima do máximo responde `400` |

Resposta `200`, forma de `PagedResult<FlowTraceSummary>`:

```json
{
  "items": [
    {
      "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
      "method": "GET",
      "routeTemplate": "/api/orders/{id}",
      "httpStatus": 200,
      "status": "Success",
      "startedAt": "2026-10-19T13:42:11.121624Z",
      "durationMicros": 18432,
      "truncated": false
    }
  ],
  "totalCount": 137
}
```

### GET `/traces/live`

Traces em andamento (`FlowStatus.Running`), direto do `FlowRecorder` em memória, nunca do
`ITraceStore`. Sem paginação: o teto é o próprio limite de 500 traces simultâneos (seção 6.7).

Resposta `200`, `FlowTraceSummary[]` (mesma forma da lista acima; `httpStatus` e
`durationMicros` vêm `null` enquanto o trace não termina).

### GET `/traces/{traceId}`

Trace consolidado com spans. Procura primeiro no `FlowRecorder` (trace ainda em andamento ou
recém-concluído), depois no `ITraceStore` configurado.

Resposta `200`:

```json
{
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "method": "GET",
  "routeTemplate": "/api/orders/{id}",
  "httpStatus": 200,
  "status": "Success",
  "startedAt": "2026-10-19T13:42:11.121624Z",
  "endedAt": "2026-10-19T13:42:11.139201Z",
  "durationMicros": 17577,
  "spanCount": 3,
  "truncated": false,
  "errorMessage": null,
  "tags": { "tenant": "acme" },
  "spans": [
    {
      "spanId": "00f067aa0ba902b7",
      "parentSpanId": null,
      "sequence": 0,
      "component": "OrdersController",
      "operation": "GetOrder",
      "type": "Controller",
      "startedAt": "2026-10-19T13:42:11.121624Z",
      "endedAt": "2026-10-19T13:42:11.139201Z",
      "durationMicros": 17577,
      "status": "Success",
      "errorType": null,
      "errorMessage": null,
      "isErrorOrigin": false,
      "tags": {}
    }
  ]
}
```

Campos de nível superior = `FlowTrace` (seção 1), mais `spans: FlowSpan[]` na ordem de
`Sequence`. `404` se o `traceId` não existir em nenhum dos dois.

### GET `/traces/{traceId}/spans/{spanId}`

Span com ancestrais, filhos e comparação com a mediana normal daquela operação
(`ComponentStat.MedianDurationMicros` para o par `Component`/`Operation` do span).

Resposta `200`:

```json
{
  "span": { "...": "forma de FlowSpan, igual à lista de spans acima" },
  "ancestors": [
    { "spanId": "...", "component": "OrdersController", "operation": "GetOrder" }
  ],
  "children": [
    { "spanId": "...", "component": "OrderRepository", "operation": "FindAsync" }
  ],
  "medianDurationMicros": 15200,
  "deltaFromMedianMicros": 2377
}
```

`ancestors` vem ordenado da raiz para o pai direto (exclui o próprio span). `children` é só um
nível (filhos diretos, não a árvore inteira — a árvore completa já está em `/traces/{traceId}`).
`medianDurationMicros` e `deltaFromMedianMicros` vêm `null` se não houver histórico suficiente
no `ITraceStore` para aquele componente/operação. `404` se o `traceId` ou o `spanId` não
existirem.

### GET `/blueprint`

Spans da última execução bem-sucedida do endpoint, usados pelo frontend para inferir etapas
"não executadas" (seção 7.2 do prompt).

Query string: `method` (obrigatório), `route` (obrigatório, route template exato).

Resposta `200`, mesma forma de `/traces/{traceId}` (trace + spans). `204` sem corpo se não
houver execução bem-sucedida anterior daquele método/rota. `400` se faltar `method` ou `route`.

### GET `/endpoints`

Endpoints rastreáveis da própria API hospedeira, via `EndpointDataSource` e, quando disponível,
o documento OpenAPI (`/openapi/v1.json` por padrão, caminho configurável).

Resposta `200`:

```json
[
  { "method": "GET", "routeTemplate": "/api/orders/{id}", "displayName": "GetOrder" }
]
```

`displayName` vem do `summary`/`operationId` do OpenAPI quando existir; senão, do nome do
método/endpoint. Lista ordenada por `routeTemplate`, depois `method`.

### GET `/metrics`

Totais da janela de tempo pedida, sobre o `ITraceStore`.

Query string: `from`, `to` (ambos opcionais, forma de `StatsQuery`).

Resposta `200`:

```json
{
  "totalTraces": 1842,
  "errorRate": 0.034,
  "averageDurationMicros": 21500,
  "p95DurationMicros": 88200,
  "maxDurationMicros": 412000
}
```

`errorRate` é fração (`0.034` = 3,4%), não percentual. Campos `*DurationMicros` vêm `0` quando
`totalTraces` é `0` (nunca `null`, para o frontend não precisar tratar ausência).

### GET `/metrics/components`

Estatísticas por componente e operação, forma de `ComponentStat[]` (`src/NetApiFlow/ComponentStat.cs`).

Query string: `from`, `to` (opcionais, forma de `StatsQuery`).

Resposta `200`:

```json
[
  {
    "component": "OrderRepository",
    "operation": "FindAsync",
    "executionCount": 412,
    "errorCount": 3,
    "medianDurationMicros": 1200,
    "averageDurationMicros": 1450,
    "p95DurationMicros": 3100,
    "maxDurationMicros": 9800
  }
]
```

Ordenado por `executionCount` decrescente.

### DELETE `/traces`

Limpa todos os dados do `ITraceStore` e os traces em andamento do `FlowRecorder`. Só em
`IHostEnvironment.IsDevelopment()`, ou com `RequireAuthorization`/`IDashboardAuthorizationFilter`
configurado (ADR-0006). `204` sem corpo em caso de sucesso. `404` fora de Development sem
autorização configurada (mesma regra do resto do dashboard, risco R-09).

### GET `/stream`

Server-Sent Events, contrato completo na seção 2 deste arquivo.

---

Autorização vale para as dez rotas acima, sem exceção (ADR-0006): fora de
`IHostEnvironment.IsDevelopment()`, sem `RequireAuthorization` nem
`IDashboardAuthorizationFilter` configurado, toda rota responde `404`. Com um dos dois
configurado e o usuário sem a claim exigida, `403` (risco R-09, mitigação na Fase 5).
