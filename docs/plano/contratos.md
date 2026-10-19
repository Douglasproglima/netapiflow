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
    ValueTask<FlowTrace?> GetAsync(string traceId, CancellationToken cancellationToken);
    ValueTask<PagedResult<FlowTraceSummary>> QueryAsync(TraceQuery query, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<ComponentStat>> GetComponentStatsAsync(StatsQuery query, CancellationToken cancellationToken);
    ValueTask<FlowTrace?> GetLastSuccessfulAsync(string method, string routeTemplate, CancellationToken cancellationToken);
    ValueTask<int> ApplyRetentionAsync(RetentionPolicy policy, CancellationToken cancellationToken);
}

public interface IFlowTenantResolver { string? Resolve(HttpContext context); }
public interface IDashboardAuthorizationFilter { ValueTask<bool> AuthorizeAsync(HttpContext context); }
```

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
- `event:` sempre presente, para o cliente filtrar sem desserializar o corpo.
- Heartbeat `: ping` a cada 15 segundos, para o proxy intermediário não derrubar a conexão por
  inatividade.
- Buffer de replay circular de 1000 eventos (seção 6.7 da tabela de limites): reconexão com
  `Last-Event-ID` dentro da janela recebe o que perdeu; fora da janela, recebe só os eventos
  novos a partir da reconexão (sem erro, sem garantia de completude total, documentado como
  limitação conhecida).

## 3. API REST

Base configurável, padrão `/flow/api`. Só DTO, JSON camelCase. Visão geral (seção 8 do prompt):

| Método | Rota | Descrição |
|---|---|---|
| GET | `/traces` | Lista paginada com filtros |
| GET | `/traces/live` | Traces em andamento |
| GET | `/traces/{traceId}` | Trace consolidado com spans (procura na memória, depois no store) |
| GET | `/traces/{traceId}/spans/{spanId}` | Span com ancestrais, filhos e comparação com a mediana |
| GET | `/blueprint?method=&route=` | Spans da última execução bem-sucedida do endpoint (204 se não houver) |
| GET | `/endpoints` | Endpoints rastreáveis (via `EndpointDataSource` e documento OpenAPI) |
| GET | `/metrics` | Totais, taxa de erro, latência média, p95 e máxima |
| GET | `/metrics/components` | Estatísticas por componente e operação |
| DELETE | `/traces` | Limpa os dados (só em Development, ou com autorização) |
| GET | `/stream` | Server-Sent Events |

As rotas do próprio NetApiFlow nunca são rastreadas (nunca aparecem como span `Endpoint` ou
`Controller` no próprio dashboard).

**Pendente, bloqueante para a Fase 4:** o contrato detalhado de cada rota (query string, formato
do corpo, DTO de resposta com exemplo JSON) é responsabilidade da tarefa 3.1 em
`fase-3-api-do-dashboard-e-sse.md`. Este arquivo é atualizado nessa tarefa, pela Planejadora, e
só então a Fase 4 (Angular) começa, porque o frontend depende desse contrato byte a byte.
