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

    /// <summary>The NetApiFlow trace id of the request currently executing, the same value as the
    /// <c>X-NetApiFlow-TraceId</c> response header, or <c>null</c> when tracing is disabled or there is no
    /// ambient trace. It is not the W3C trace id: read that from <c>Activity.Current.TraceId</c>.</summary>
    public static string? CurrentTraceId { get; }

    /// <summary>A trace that finished recently, read straight from memory, without waiting for
    /// the persistence queue. Built for integration tests asserting on the trace their own
    /// request just produced (D-58 item 6): read <c>X-NetApiFlow-TraceId</c> off the response,
    /// then call this. Only looks at the in-memory buffer, never falls back to
    /// <see cref="ITraceStore"/>: <c>null</c> if the trace already aged out of it.</summary>
    public static FlowTraceDetail? GetRecentTrace(IServiceProvider services, string traceId);
}

public readonly struct FlowStep : IDisposable
{
    public void Tag(string key, string value);
    public void MarkError(Exception exception);
    public void Dispose();
}

/// <summary>A single <c>ILogger</c> call correlated to the span active when it happened (ADR-0017).
/// Never populates <c>FlowSpan.Tags</c>: a separate field, its own opt-in, its own sanitization.</summary>
public sealed record LogEntry
{
    public required DateTime Timestamp { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
    public string? ExceptionType { get; init; }
    public string? ExceptionMessage { get; init; }
}

public enum ComponentType { Endpoint, Controller, UseCase, Service, Repository, Database, Http, Custom }
public enum FlowStatus { Running, Success, Error, Cancelled, Timeout }

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
    ValueTask<IReadOnlyList<RouteStat>> GetRouteStatsAsync(StatsQuery query, CancellationToken cancellationToken);
    ValueTask<ArchitectureMap> GetArchitectureMapAsync(string method, string routeKey, StatsQuery query, CancellationToken cancellationToken);
    ValueTask<FlowTraceDetail?> GetLastSuccessfulAsync(string method, string routeKey, CancellationToken cancellationToken);
    ValueTask<FlowTraceDetail?> GetLastSuccessfulAsync(string method, string routeKey, DateTime before, CancellationToken cancellationToken);
    ValueTask<int> ApplyRetentionAsync(RetentionPolicy policy, CancellationToken cancellationToken);
}

public sealed record DeployAnnotation
{
    public required string Id { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Label { get; init; }
}

public interface IDeployAnnotationStore
{
    ValueTask SaveAsync(DeployAnnotation annotation, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<DeployAnnotation>> QueryAsync(DateTime from, DateTime until, CancellationToken cancellationToken);
}

public interface IFlowTenantResolver { string? Resolve(HttpContext context); }
```

`IDeployAnnotationStore` (Fase 28, D-60) é irmã de `ITraceStore`, não extensão dela: marca um
deploy na timeline do histórico, compartilhado entre todo mundo que abre o dashboard, nunca só no
navegador de quem registrou o marco. `Timestamp` é sempre atribuído pelo servidor, nunca recebido
do cliente. Sem `DeleteAsync` (YAGNI até aparecer um pedido real de apagar um marco).
`InMemoryDeployAnnotationStore` é a implementação padrão do núcleo;
`AddNetApiFlowPostgreSqlStorage`/`AddNetApiFlowSqlServerStorage` substituem os dois stores
(`ITraceStore` e `IDeployAnnotationStore`) na mesma chamada, mesmo backing que os traces já usam.

```csharp
/// <summary>Shared gate for every diagnostic surface (Dashboard, MCP, any future one). ADR-0015.</summary>
public interface IFlowAccessFilter { ValueTask<bool> AuthorizeAsync(HttpContext context); }

public sealed class NetApiFlowOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan SlowThreshold { get; set; } = TimeSpan.FromMilliseconds(250);
    public bool CaptureSqlText { get; set; }
    public ExternalActivitySource ExternalSources { get; set; } = ExternalActivitySource.None;

    /// <summary>Captures Authorization and Resource filter stages (MVC Controllers only) as
    /// steps, via <c>DiagnosticListener</c>/native filter (ADR-0019). Default: <see langword="false"/>.</summary>
    public bool CapturePrePipelineSteps { get; set; }

    /// <summary>Root-relative path of the OpenAPI document the dashboard's "Run an endpoint"
    /// panel reads. Default: <c>/openapi/v1.json</c>, what <c>MapOpenApi()</c> serves. Requests to
    /// this path are never traced.</summary>
    public string OpenApiDocumentPath { get; set; } = "/openapi/v1.json";

    /// <summary>Writes <c>X-NetApiFlow-TraceId</c> on the response of every traced request, so a
    /// caller can find the trace and <c>NetApiFlow.Testing</c> can read it. Default:
    /// <see langword="true"/>. Set it to <see langword="false"/> to keep the trace id out of
    /// responses.</summary>
    public bool EmitTraceIdHeader { get; set; } = true;

    /// <summary>Opens an <see cref="ComponentType.Endpoint"/> step for every Minimal API endpoint,
    /// without <c>WithNetApiFlowTracking()</c>. Default: <see langword="true"/>. An endpoint that is
    /// already marked keeps a single step, and an MVC action keeps only the controller step.</summary>
    public bool CaptureMinimalApiEndpoints { get; set; } = true;
    public RetentionOptions Retention { get; }
    public LimitsOptions Limits { get; }
    public TailSamplingOptions TailSampling { get; }
    public LogCorrelationOptions LogCorrelation { get; }
    public IReadOnlyList<string> IncludePatterns { get; }
    public IReadOnlyList<string> ExcludePatterns { get; }
    public NetApiFlowOptions IncludePaths(params string[] patterns);
    public NetApiFlowOptions ExcludePaths(params string[] patterns);
}

/// <summary>Tail-based sampling of persisted traces (D-82). Default: disabled, every completed
/// trace is persisted, same behavior as before this option existed.</summary>
public sealed class TailSamplingOptions
{
    /// <summary>Enables tail-based sampling. Default: <see langword="false"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Fraction (0.0-1.0) of successful, non-slow traces to persist. A trace with
    /// <see cref="FlowStatus.Error"/> or <see cref="FlowStatus.Timeout"/>, or a duration at or
    /// above <see cref="NetApiFlowOptions.SlowThreshold"/>, is always persisted, regardless of
    /// this value. A <see cref="FlowStatus.Cancelled"/> trace follows the same sampling rate as
    /// <see cref="FlowStatus.Success"/> (ADR-0020): a client disconnecting is routine churn, not
    /// a server-side problem worth guaranteeing coverage for. Default: <c>0.1</c> (keep 10%).</summary>
    public double SuccessSampleRate { get; set; } = 0.1;
}

/// <summary>Log and exception correlation per step, via an internal <c>ILoggerProvider</c> (ADR-0017).
/// Default: disabled.</summary>
public sealed class LogCorrelationOptions
{
    /// <summary>Enables log correlation. Default: <see langword="false"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Minimum <see cref="Microsoft.Extensions.Logging.LogLevel"/> captured. Default:
    /// <see cref="Microsoft.Extensions.Logging.LogLevel.Warning"/>.</summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Warning;
}

/// <summary>Ecosystem <c>ActivitySource</c> instances NetApiFlow also listens to (ADR-0012). Opt-in, "None" by default.</summary>
[Flags]
public enum ExternalActivitySource
{
    None = 0,

    /// <summary>Outbound calls from <see cref="System.Net.Http.HttpClient"/>, mapped to a <see cref="ComponentType.Http"/> span.</summary>
    HttpClient = 1,

    /// <summary>SQL commands from Npgsql's own <c>ActivitySource</c> (ADR-0018), mapped to a
    /// <see cref="ComponentType.Database"/> span, recorded as a child of the step that issued the
    /// command. Dropped only when a <see cref="ComponentType.Database"/> step already wraps the call
    /// (the one <c>NetApiFlow.EntityFrameworkCore</c> opens), checked through
    /// <see cref="System.Diagnostics.Activity.Parent"/>, so a command is never recorded twice.</summary>
    Npgsql = 2,

    /// <summary>SQL commands from Microsoft.Data.SqlClient's own <see cref="System.Diagnostics.DiagnosticListener"/>
    /// (the driver never exposes an <c>ActivitySource</c>, unlike Npgsql; confirmed by the Fase 37
    /// investigation, correcting this contract's original text), mapped to a
    /// <see cref="ComponentType.Database"/> span, recorded as a child of the step that issued the
    /// command and dropped under the same rule as <see cref="Npgsql"/>: only when a
    /// <see cref="ComponentType.Database"/> step already wraps it. The driver's event
    /// payload is only strongly typed from version 6.0 on; on an older pinned version the listener
    /// recognizes the gap and records nothing, rather than failing.</summary>
    SqlClient = 4,
}

public sealed class RetentionOptions
{
    public int MaxTraces { get; set; } = 1000;
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(24);
}

public sealed class LimitsOptions
{
    public int MaxSpansPerTrace { get; set; } = 300;

    /// <summary>Maximum log entries correlated per span (ADR-0017), before extra entries are
    /// discarded. Default: 20.</summary>
    public int MaxLogEntriesPerSpan { get; set; } = 20;

    /// <summary>Maximum custom tags recorded per span, before extra tags are discarded.
    /// Default: 20.</summary>
    public int MaxTagsPerSpan { get; set; } = 20;

    /// <summary>Maximum length of a single tag value, before it is truncated. Default: 4000.</summary>
    public int MaxTagValueLength { get; set; } = 4000;
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

    /// <summary>Tracks the application's own services, repositories and handlers by name:
    /// <c>*Service</c> as <see cref="ComponentType.Service"/>, <c>*Repository</c> as
    /// <see cref="ComponentType.Repository"/>, <c>*Handler</c> as <see cref="ComponentType.UseCase"/>.
    /// Only types of the entry assembly, or of the assemblies and namespaces named in
    /// <paramref name="configure"/>, are ever decorated, never a framework, third-party or NetApiFlow
    /// type. Call it after your own registrations: a registration made later is not decorated, and
    /// the host logs a warning naming it. A registration made by factory cannot be decorated, and the
    /// host logs a warning naming it too.</summary>
    public static IServiceCollection TrackFlowByConvention(
        this IServiceCollection services, Action<FlowConventionOptions>? configure = null);
}

public sealed class FlowConventionOptions
{
    /// <summary>Adds an assembly to the scope. Any call replaces the default scope, the entry assembly.</summary>
    public FlowConventionOptions IncludeAssembly(Assembly assembly);

    /// <summary>Adds a namespace prefix to the scope. Any call replaces the default scope, the entry assembly.</summary>
    public FlowConventionOptions IncludeNamespacePrefix(string prefix);
}

public static class NetApiFlowEndpointFilterExtensions
{
    public static RouteHandlerBuilder WithNetApiFlowTracking(this RouteHandlerBuilder builder);
    public static RouteGroupBuilder WithNetApiFlowTracking(this RouteGroupBuilder builder);
}

public static class NetApiFlowEndpointConventionExtensions
{
    /// <summary>Marks the endpoint, or every endpoint of a group, as never traced. Read from the matched endpoint's
    /// metadata, so it holds under <c>UsePathBase</c>.</summary>
    public static TBuilder WithoutNetApiFlowTracking<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder;
}
```

`NetApiFlow.MediatR` expõe `AddNetApiFlowMediatR(this IServiceCollection)` (registra o
`IPipelineBehavior<,>` que gera span `UseCase`, além do `AddMediatR(...)` do próprio MediatR).
`NetApiFlow.EntityFrameworkCore` expõe `AddNetApiFlowEntityFrameworkCore(this IServiceCollection)`
(registra o interceptor; ainda precisa de `optionsBuilder.AddInterceptors(...)` no `DbContext`,
porque o EF Core não tem registro global de interceptor). Formalizado na revisão da Fase 2.

**Chamadas repetidas (Fase 66, D-276).** `AddNetApiFlow`, `AddNetApiFlowDashboard`, `AddNetApiFlowMediatR` e
`AddNetApiFlowOtlpExport` são seguros de chamar mais de uma vez. O `configure` de cada chamada vale, na ordem das chamadas, sobre
um único objeto de opções, e nada é registrado duas vezes: um listener, um exportador, um step por requisição. Uma única chamada
dá o mesmo resultado de antes.

Formalizado na revisão da Fase 1 (estava ausente desta lista desde o bootstrap, embora a seção 9
do prompt já descrevesse essa forma de configuração em código).

**Identidade da trace (ADR-0028).** `FlowTrace.TraceId` é um id próprio de cada requisição rastreada, aleatório, de
32 caracteres hexadecimais, gerado pelo NetApiFlow. **Não** é o W3C trace id: este fica na tag `w3c.trace_id` da
trace, e toda atividade do NetApiFlow carrega a tag `netapiflow.trace_id`. O cabeçalho `X-NetApiFlow-TraceId`, o
REST, o SSE, o MCP, `NetApiFlow.CurrentTraceId` e o `traceId` de `NetApiFlow.GetRecentTrace` usam o id do
NetApiFlow. Duas requisições com o mesmo W3C trace id são duas traces.

**Exclusão por metadado (ADR-0027, Fase 53).** `WithoutNetApiFlowTracking()` grava um metadado interno no endpoint, e o núcleo decide no `EndpointMatched`, que é onde o `Request.Path` já é relativo ao `PathBase`. O painel (REST, SSE, estáticos) e o MCP a chamam nos grupos deles, e é por isso que as rotas do NetApiFlow nunca são rastreadas. A extensão é genérica sobre `IEndpointConventionBuilder` (cobre `MapGet`, grupo, `MapControllers()` e `MapHealthChecks()`), o tipo do metadado é `internal`, e o metadado **vence** `ExcludePaths`, `IncludePaths` e `WithNetApiFlowTracking()` no mesmo endpoint. Controller por atributo não existe.

**O que a Fase 53 entregou, no contrato (#420, D-258).** `ExcludePaths`, `IncludePaths` e `OpenApiDocumentPath` valem contra o caminho relativo ao `PathBase` quando há endpoint casado, e contra o caminho cheio quando nenhum endpoint casa (um arquivo estático, um 404), que é o limite dito no README. A decisão de uma requisição reexecutada (`UseStatusCodePagesWithReExecute`, `UseExceptionHandler`) segue a da requisição original, como na `0.5.3`. **Duas mudanças visíveis:** (1) a trace de uma requisição que casa um endpoint abre já com a rota conhecida, então `TraceStarted`, a lista ao vivo e a lista REST mostram `/orders/{id}` onde mostravam `(unmatched)` até o fim; (2) uma requisição que não casa nenhum endpoint (middleware terminal, WebSocket) é gravada **só quando termina**, junto com os spans dela, e não aparece em `/traces/live`, no stream nem em `GET /traces/{traceId}` enquanto roda (D-256). A API pública nova é só a extensão acima.

**Nome da etapa `Endpoint` de um Minimal API (Tarefa 51.9).** O par `(component, operation)` sai de
metadados do endpoint, nunca do `DisplayName`, e é o mesmo para a etapa de `WithNetApiFlowTracking()` e
para a etapa sem marcação. Método com nome, sem marca de compilador: `Tipo.Método`. Lambda ou função
local: `component = "Minimal API"` e `operation` com o nome de `WithName` ou, sem ele, os verbos e a
rota normalizada (`GET /hello`). Os verbos se juntam com `, ` (`GET, PUT /rota`), e um endpoint sem metadado de verbo traz só a rota. Um
endpoint que não é `RouteEndpoint` grava `component = "Minimal API"` e `operation` com o `DisplayName`, ou
`Endpoint` se ele for nulo. Controller de MVC e Razor Pages mantêm `Controller.Action`. A regra e as
medições estão em `docs/plano/fase-51-sem-marcacao.md`, Tarefa 51.9.

### `NetApiFlow.Dashboard`: superfície pública

```csharp
namespace NetApiFlow.Dashboard;

public static class NetApiFlowDashboardServiceCollectionExtensions
{
    public static IServiceCollection AddNetApiFlowDashboard(this IServiceCollection services);
}

namespace NetApiFlow.Dashboard.Endpoints;

public static class NetApiFlowDashboardEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder UseNetApiFlowDashboard(
        this IEndpointRouteBuilder endpoints, string basePath = "/flow");
}
```

`UseNetApiFlowDashboard` monta a API REST/SSE sob `{basePath}/api` e a UI Angular embutida sob
`basePath`, com fallback de SPA. Devolve um `IEndpointConventionBuilder` para encadear
`RequireAuthorization(...)`, que vale para as duas partes juntas (ADR-0006). Fora de
`IHostEnvironment.IsDevelopment()`, sem `RequireAuthorization` nem `IFlowAccessFilter` (núcleo,
ADR-0015; antes `IDashboardAuthorizationFilter`, declarado localmente aqui até a Fase 10)
registrado no DI, toda rota do dashboard responde `404`; com um dos dois e a autorização negada,
responde `403`. Tudo que não está nesta lista (`DashboardAccessGate`,
`DashboardIndexHtmlCache`, `DashboardEmbeddedFiles`, `MapNetApiFlowDashboardApi`) é `internal`.
Formalizado na revisão da Fase 5.

### `NetApiFlow.Mcp`: superfície pública

```csharp
namespace NetApiFlow.Mcp;

public static class NetApiFlowMcpServiceCollectionExtensions
{
    public static IServiceCollection AddNetApiFlowMcp(this IServiceCollection services);
}

namespace NetApiFlow.Mcp.Endpoints;

public static class NetApiFlowMcpEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder UseNetApiFlowMcp(
        this IEndpointRouteBuilder endpoints, string basePath = "/flow/mcp");
}
```

### `NetApiFlow.Testing`: superfície pública

Congelada pelo ADR-0022, implementada na Fase 39. Sem dependência de terceiro: só referencia
`NetApiFlow` (núcleo), de propósito, para não impor Shouldly (ou qualquer outro framework de
asserção) ao projeto do usuário.

```csharp
namespace NetApiFlow.Testing;

public static class FlowTraceAssertions
{
    public static FlowTraceDetail ShouldHaveStatus(this FlowTraceDetail trace, FlowStatus status);
    public static FlowTraceDetail ShouldHaveNoErrors(this FlowTraceDetail trace);
    public static FlowTraceDetail ShouldCallAtMost(this FlowTraceDetail trace, ComponentType type, int maxCount);
    public static FlowTraceDetail ShouldNeverCall(this FlowTraceDetail trace, string component, string? operation = null);
    public static FlowTraceDetail ShouldHaveCalled(this FlowTraceDetail trace, string component, string? operation = null);
}

/// <summary>Thrown by <see cref="FlowTraceAssertions"/> and <see cref="FlowTestingHttpResponseExtensions"/>
/// on assertion failure. Left uncaught, any test framework reports it as a failed test.</summary>
public sealed class FlowAssertionException : Exception
{
    public FlowAssertionException(string message);
}

public static class FlowTestingHttpResponseExtensions
{
    /// <summary>Reads <c>X-NetApiFlow-TraceId</c> off the response and calls
    /// <see cref="NetApiFlow.GetRecentTrace"/>, throwing <see cref="FlowAssertionException"/>
    /// instead of returning <see langword="null"/> when the trace is not found.</summary>
    public static FlowTraceDetail GetFlowTrace(this HttpResponseMessage response, IServiceProvider services);
}
```

`UseNetApiFlowMcp` monta o servidor MCP (transporte HTTP, `ModelContextProtocol.AspNetCore`) sob
`basePath`. Mesma régua da Dashboard (ADR-0006/ADR-0013), com o filtro compartilhado do núcleo
(ADR-0015): fora de `IHostEnvironment.IsDevelopment()`, sem `RequireAuthorization(...)` nem
`IFlowAccessFilter` registrado no DI, toda rota do MCP responde `404`. Três tools na v1, todas só
leitura, implementadas sobre `ITraceStore`: listar traces recentes (filtro por status, método,
rota, limite), obter uma trace por id, obter estatísticas por componente. Não depende de
`NetApiFlow.Dashboard`: os dois dependem só de `NetApiFlow` (núcleo) para `IFlowAccessFilter`.
Formalizado na revisão da Fase 10.

### `NetApiFlow.OpenTelemetry`: superfície pública

```csharp
namespace NetApiFlow.OpenTelemetry;

public static class NetApiFlowOpenTelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddNetApiFlowOtlpExport(
        this IServiceCollection services,
        Action<NetApiFlowOtlpExportOptions>? configure = null);
}

public sealed class NetApiFlowOtlpExportOptions
{
    public bool IncludeHttpClientSource { get; set; }
    public Action<OtlpExporterOptions>? ConfigureOtlpExporter { get; set; }
}

public static class NetApiFlowTracerProviderBuilderExtensions
{
    public static TracerProviderBuilder AddNetApiFlowSource(this TracerProviderBuilder builder);
}
```

`AddNetApiFlowOtlpExport` registra o SDK do OpenTelemetry (`AddOpenTelemetry().WithTracing(...)`)
observando a fonte `"NetApiFlow"` (a mesma `ActivitySource` do núcleo, ADR-0002) e, se
`IncludeHttpClientSource` for `true`, também `"System.Net.Http"` (ADR-0012), antes de
`AddOtlpExporter`. Não lê nem reescreve `FlowSpan`/`ITraceStore`: exporta a `Activity` nativa
diretamente, sem depender do pipeline do dashboard. ADR-0016. Formalizado na revisão da Fase 13.

`AddNetApiFlowSource` só faz `AddSource("NetApiFlow")`: nenhum exportador e nenhuma outra fonte. É a forma para quem **já**
configura o OpenTelemetry, e os spans do NetApiFlow vão só para os exportadores do app (Fase 66, B7, D-276).
`AddNetApiFlowOtlpExport` não mudou o que exporta: num app com OpenTelemetry próprio, ele manda **todas** as fontes do app para o
endpoint do NetApiFlow, e o README avisa disso. Ele usa `AddNetApiFlowSource` para a fonte `"NetApiFlow"`, e
`IncludeHttpClientSource` passa a ser lido quando o provider é montado, não na chamada.

### `NetApiFlow.Storage.PostgreSql` e `NetApiFlow.Storage.SqlServer`: superfície pública

```csharp
namespace NetApiFlow.Storage.PostgreSql;

public static class NetApiFlowPostgreSqlServiceCollectionExtensions
{
    public static IServiceCollection AddNetApiFlowPostgreSqlStorage(
        this IServiceCollection services, string connectionString,
        Action<PostgreSqlStorageOptions>? configure = null);
}

public sealed class PostgreSqlStorageOptions
{
    public string TablePrefix { get; set; } = "netapiflow_";

    /// <summary>Creates and upgrades the tables on first use, with a <c>CREATE TABLE</c>, an
    /// <c>ALTER TABLE</c> and an <c>UPDATE</c> against the application's database. Default:
    /// <see langword="true"/>. With <see langword="false"/> nothing is created or changed and the
    /// tables must already exist; <c>schema.sql</c> in the package's repository folder creates them.</summary>
    public bool AutoCreateSchema { get; set; } = true;
}

// NetApiFlow.Storage.SqlServer espelha a mesma forma: AddNetApiFlowSqlServerStorage(
// this IServiceCollection, string connectionString, Action<SqlServerStorageOptions>?)
// e SqlServerStorageOptions com o mesmo TablePrefix e o mesmo AutoCreateSchema.
```

Chamar depois de `AddNetApiFlow(...)`: substitui o `ITraceStore` em memória pelo storage
persistente (`services.Replace(...)`, singleton). `TablePrefix` é validado contra
`^[A-Za-z_][A-Za-z0-9_]*$` antes de compor nome de tabela (identificador não é parametrizável em
SQL); prefixo inválido lança `ArgumentException` na chamada de `Add...Storage`, não em tempo de
query. Tudo que não está nesta lista (`PostgreSqlTraceStore`, `SqlServerTraceStore`,
`*SchemaInitializer`, `*TableNames`) é `internal`. Formalizado na revisão da Fase 6.

### DTOs de apoio de `ITraceStore` (provisórios até a Fase 3)

`PagedResult<T>`, `FlowTraceSummary`, `TraceQuery`, `ComponentStat`, `StatsQuery` e
`RetentionPolicy` existem desde a Fase 1 com a forma implementada em `src/NetApiFlow/*.cs`,
cobrindo os filtros da seção 7.1 item 3 (status, método, rota, tenant, período, duração mínima,
paginação) e as métricas da seção 7.1 item 4 (execuções, erros, mediana, média, p95, máximo).
Cada um traz `/// <remarks>Provisional shape...</remarks>` no comentário XML. O contrato
definitivo (campo a campo, JSON de exemplo) continua sendo responsabilidade da tarefa 3.1.
A Fase 3 pode renomear ou reformar esses tipos sem que isso seja uma quebra de contrato desta
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
`0` quando `TotalTraces` é `0`), não pelo `ITraceStore`. `ErrorCount` (aqui, em `ComponentStat` e
em `RouteStat`) conta `FlowStatus.Error` **e** `FlowStatus.Timeout`, nunca `FlowStatus.Cancelled`
(ADR-0020): um cliente cancelando não indica problema no servidor, um timeout interno indica.

`GetRouteStatsAsync` e `RouteStat` foram acrescentados a `ITraceStore` na Fase 14 (D-81): agregado
por rota do trace (`Method`, `RouteTemplate`), mesmo molde de `ComponentStat` mas sobre
`FlowTrace`, não `FlowSpan`. Base da visão agregada estilo Pulse (`GET /metrics/routes`).

```csharp
public sealed record RouteStat
{
    public required string Method { get; init; }
    public required string RouteTemplate { get; init; }
    public required int ExecutionCount { get; init; }
    public required int ErrorCount { get; init; }
    public required long MedianDurationMicros { get; init; }
    public required long AverageDurationMicros { get; init; }
    public required long P95DurationMicros { get; init; }
    public required long MaxDurationMicros { get; init; }
}
```

`RouteTemplate` é sempre normalizado (D-124): barra inicial, sem barra final, preservando o
restante do texto do `RoutePattern` como vem (incluindo parâmetro e restrição, ex.
`/api/orders/{id:int}`). A normalização acontece na gravação (middleware) e em todo ponto de
entrada que recebe um `route` de fora (`/traces`, `/blueprint`, `GET /endpoints`), usando o mesmo
helper interno. Nenhum `ITraceStore` precisa normalizar por conta própria pra essa parte: ao
chegar nele, o valor já está no formato canônico (barra) dos dois lados da comparação.

**`GetArchitectureMapAsync`/`GetLastSuccessfulAsync` recebem `routeKey`, não `routeTemplate`
(D-164): o parâmetro mudou de nome porque a comparação ignora restrição de parâmetro, não é mais
igualdade exata de string.** O valor que o chamador passa pode vir de um documento OpenAPI
(Runner), que nunca carrega restrição (`:int`, `:guid`, `?`, catch-all); o valor gravado em
`FlowTrace.RouteTemplate` sempre carrega, quando a rota declarou uma. Sem esse tratamento,
`/api/orders/{id}` (Runner) nunca encontraria execuções gravadas como `/api/orders/{id:int}`.
`NetApiFlow.Storage.PostgreSql` e `NetApiFlow.Storage.SqlServer` resolvem isso com uma coluna
interna adicional (`route_key`, nunca exposta em nenhum DTO público), computada pelo mesmo helper
que normaliza a barra; o chamador (`NetApiFlow.Dashboard`) normaliza o valor recebido antes de
repassar. **Um `ITraceStore` próprio** (fora dos dois pacotes oficiais) precisa replicar esse
comportamento nesses dois métodos especificamente: comparar ignorando restrição de parâmetro, não
assumir igualdade de string com `RouteTemplate`. Os outros métodos da interface (`QueryAsync`,
`GetRouteStatsAsync`, etc.) continuam recebendo `routeTemplate` e comparando pelo valor gravado
como está, sem essa segunda normalização.

`GetArchitectureMapAsync` e `ArchitectureMap` foram acrescentados a `ITraceStore` na Fase 16
(D-83): grafo estático da rota (`method`, `routeTemplate`), união de todas as execuções
registradas na janela de `StatsQuery`, não um trace em particular. Nó por `(Component,
Operation)` distinto observado; aresta por relação pai-filho distinta observada entre nós.
`ExecutionCount` de um nó é quantas execuções da rota, na janela, passaram por aquele passo;
`TotalExecutions` é o total de execuções da rota na janela (mesmo denominador para todo nó e
aresta); o nó raiz (sem pai) tem sempre `ExecutionCount == TotalExecutions`. Base do mapa de
arquitetura vivo (D-60 item E).

```csharp
public sealed record ArchitectureNode
{
    public required string Component { get; init; }
    public required string Operation { get; init; }
    public required ComponentType Type { get; init; }
    public required int ExecutionCount { get; init; }
}

public sealed record ArchitectureEdge
{
    public required string FromComponent { get; init; }
    public required string FromOperation { get; init; }
    public required string ToComponent { get; init; }
    public required string ToOperation { get; init; }
    public required int CallCount { get; init; }
}

public sealed record ArchitectureMap
{
    public required int TotalExecutions { get; init; }
    public required IReadOnlyList<ArchitectureNode> Nodes { get; init; }
    public required IReadOnlyList<ArchitectureEdge> Edges { get; init; }
}
```

`TotalExecutions`, `Nodes` e `Edges` vêm vazios/zero (`TotalExecutions = 0`, as duas listas
vazias) quando a rota não tem nenhuma execução na janela, nunca `null`: mesma convenção de
`/metrics` (campos de duração vêm `0`, nunca ausentes).

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
    "tags": {},
    "logs": []
  }
}
```

Eventos de `TraceStarted` e `TraceCompleted` trazem a chave `trace` (forma de `FlowTrace`) em vez
de `span`. Os seis tipos de evento são os da seção 6.5 do prompt: `TraceStarted`, `SpanStarted`,
`SpanCompleted`, `SpanFailed`, `TraceCompleted`, `TracePersisted`.

O `TraceStarted` de uma requisição que casa um endpoint traz a rota real. Uma requisição que não casa nenhum endpoint emite o `TraceStarted`, e os eventos dos spans dela, só quando termina (D-256).

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
| `route` | string | não | `FlowTrace.RouteTemplate`, exato (não é glob), normalizado antes de comparar (D-124) |
| `tenant` | string | não | tag `tenant` do trace (seção 6.9 do prompt) |
| `from` | ISO 8601 | não | `StartedAt >= from` |
| `to` | ISO 8601 | não | `StartedAt <= to` |
| `minDurationMicros` | long | não | `DurationMicros >= minDurationMicros` |
| `page` | int, padrão `1` | não | 1-based |
| `pageSize` | int, padrão `50`, máximo `200` | não | acima do máximo responde `400` |
| `sort` | `startedAt` \| `duration` \| `httpStatus` \| `method` \| `route` | não | ordena por este campo (D-41, tarefa 7.9) |
| `order` | `asc` \| `desc` | não | direção da ordenação (D-41) |

`sort`/`order` mapeiam para `TraceQuery.SortBy`/`TraceQuery.SortDirection`
(`TraceSortField`/`SortDirection`, ambos opcionais, aditivos à tarefa 3.1). Sem `sort`, o
comportamento é o de hoje: `StartedAt` decrescente. Valor fora da lista em `sort` ou `order`
responde `400` (`ProblemDetails` citando os valores aceitos), nunca ignora em silêncio.

Regra igual nos três `ITraceStore` (`InMemoryTraceStore`, PostgreSQL, SQL Server), provada por
`TraceStoreTestsBase` contra os três:

- Desempate fixo: `StartedAt` decrescente, depois `TraceId`. Sem isso, linhas com valor igual
  trocam de página entre uma consulta e outra.
- `null` sempre por último, nas duas direções (`durationMicros` de trace em andamento,
  `httpStatus` ausente).
- `route` ordena pelo template, comparação ordinal, sem diferenciar maiúscula de minúscula.
- SQL: coluna do `ORDER BY` vem de um `switch` sobre o enum já convertido, nunca de texto da
  query string concatenado.
- `duration_micros` ganha índice em `schema.sql` dos dois bancos (Postgres e SQL Server).

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
      "tags": {},
      "logs": [
        {
          "timestamp": "2026-10-19T13:42:11.130000Z",
          "level": "Warning",
          "message": "Order total recalculated after a discount rule conflict",
          "exceptionType": null,
          "exceptionMessage": null
        }
      ]
    }
  ]
}
```

`logs` (Fase 17, ADR-0017) é só preenchido com `NetApiFlowOptions.LogCorrelation.Enabled = true`
(padrão `false`); com a feature desligada, vem sempre `[]`, nunca ausente. `message` e
`exceptionMessage` passam pelo mesmo `ErrorMessageSanitizer` já usado em `errorMessage`.

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
nível (filhos diretos, não a árvore inteira: a árvore completa já está em `/traces/{traceId}`).
`medianDurationMicros` e `deltaFromMedianMicros` vêm `null` se não houver histórico suficiente
no `ITraceStore` para aquele componente/operação. `404` se o `traceId` ou o `spanId` não
existirem.

### GET `/blueprint`

Spans da última execução bem-sucedida do endpoint, usados pelo frontend para inferir etapas
"não executadas" (seção 7.2 do prompt).

Query string: `method` (obrigatório), `route` (obrigatório, route template exato, normalizado
antes de comparar, D-124), `before` (opcional, data e hora em ISO 8601, normalmente o `startedAt`
da trace que o painel desenha).

Com `before`, a resposta é a última execução bem-sucedida que começou **estritamente antes** dele,
então uma trace nunca é o blueprint dela mesma e uma execução posterior não muda o desenho de uma
anterior. Sem `before`, a mais recente, como antes. O membro `GetLastSuccessfulAsync(method,
routeKey, before, cancellationToken)` do `ITraceStore` é de interface com implementação padrão,
para um storage de um release anterior continuar carregando (D-246, D-248). O `before` do store é
UTC.

Resposta `200`, mesma forma de `/traces/{traceId}` (trace + spans). `204` sem corpo se não
houver execução bem-sucedida anterior daquele método/rota (e, com `before`, anterior a ele).
`400` se faltar `method` ou `route`, ou se `before` não for uma data e hora válidas.

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
  "errorCount": 63,
  "errorRate": 0.034,
  "averageDurationMicros": 21500,
  "p95DurationMicros": 88200,
  "maxDurationMicros": 412000
}
```

`errorCount` é a contagem bruta (D-30): `TraceStats.ErrorCount` já existe em `ITraceStore` desde a
tarefa 3.2 (D-18), a API só não o repassava no payload, obrigando quem consome `/metrics` a
reconstruir um inteiro a partir de `errorRate` por arredondamento. `errorRate` continua no payload
(`ErrorCount / TotalTraces`, fração, `0.034` = 3,4%, não percentual), para quem só precisa da taxa
não quebrar. Campos `*DurationMicros` vêm `0` quando `totalTraces` é `0` (nunca `null`, para o
frontend não precisar tratar ausência).

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

### GET `/metrics/routes`

Acrescentada na Fase 14 (D-81), base da visão agregada estilo Pulse (D-60). Estatísticas por
rota da própria API hospedeira, forma de `RouteStat[]`.

Query string: `from`, `to` (opcionais, forma de `StatsQuery`, mesmo contrato de `/metrics/components`).

Resposta `200`:

```json
[
  {
    "method": "GET",
    "routeTemplate": "/api/orders/{id}",
    "executionCount": 612,
    "errorCount": 9,
    "medianDurationMicros": 18200,
    "averageDurationMicros": 21500,
    "p95DurationMicros": 88200,
    "maxDurationMicros": 412000
  }
]
```

Ordenado por `executionCount` decrescente, mesma convenção de `/metrics/components`. "Rotas mais
lentas" e "top erros" são ordenações diferentes do mesmo payload, feitas pelo frontend, não dois
endpoints separados. "Rotas mortas" (rota sem nenhuma execução na janela) não é campo deste
payload: o frontend cruza esta lista com `GET /endpoints`, que já lista toda rota rastreável da
API hospedeira independente de ter sido executada ou não.

### GET `/architecture`

Acrescentada na Fase 16 (D-83), base do mapa de arquitetura vivo (D-60 item E). Mapa estático de
uma rota, forma de `ArchitectureMap`.

Query string: `method` (obrigatório), `route` (obrigatório, route template exato, normalizado
antes de comparar como todo `route` desta API, D-124, mesma convenção de `/blueprint`), `from`/`to`
(opcionais, forma de `StatsQuery`).

Resposta `200`:

```json
{
  "totalExecutions": 412,
  "nodes": [
    { "component": "OrdersController", "operation": "GetOrder", "type": "Controller", "executionCount": 412 },
    { "component": "OrderService", "operation": "FindAsync", "type": "Service", "executionCount": 412 },
    { "component": "OrderCache", "operation": "TryGetAsync", "type": "Custom", "executionCount": 58 },
    { "component": "OrderRepository", "operation": "FindAsync", "type": "Repository", "executionCount": 354 }
  ],
  "edges": [
    { "fromComponent": "OrdersController", "fromOperation": "GetOrder", "toComponent": "OrderService", "toOperation": "FindAsync", "callCount": 412 },
    { "fromComponent": "OrderService", "fromOperation": "FindAsync", "toComponent": "OrderCache", "toOperation": "TryGetAsync", "callCount": 58 },
    { "fromComponent": "OrderService", "fromOperation": "FindAsync", "toComponent": "OrderRepository", "toOperation": "FindAsync", "callCount": 354 }
  ]
}
```

`400` se faltar `method` ou `route`, mesma régua de `/blueprint`. Nunca `404`/`204`: rota sem
nenhuma execução na janela devolve `200` com `totalExecutions: 0` e as duas listas vazias, mesma
convenção de `/metrics`. A percentagem de ocorrência de cada nó/aresta (`executionCount`/
`callCount` dividido por `totalExecutions`) é calculada pelo frontend, não pela API, mesmo
tratamento de `errorRate` em `/metrics`.

### DELETE `/traces`

Limpa todos os dados do `ITraceStore` e os traces em andamento do `FlowRecorder`. Só em
`IHostEnvironment.IsDevelopment()`, ou com `RequireAuthorization`/`IDashboardAuthorizationFilter`
configurado (ADR-0006). `204` sem corpo em caso de sucesso. `404` fora de Development sem
autorização configurada (mesma regra do resto do dashboard, risco R-09).

### GET `/stream`

Server-Sent Events, contrato completo na seção 2 deste arquivo.

### GET `/config`

Acrescentada na Fase 4 (D-24): expõe ao frontend valores de `NetApiFlowOptions` que afetam
apresentação, hoje só `SlowThreshold`. Sem query string, sem corpo.

```json
{
  "slowThresholdMicros": 250000,
  "version": "0.1.0",
  "tailSamplingEnabled": false,
  "successSampleRate": 0.1,
  "openApiDocumentPath": "/openapi/v1.json",
  "hostPathBase": ""
}
```

`version` (D-33) é lido de `AssemblyInformationalVersionAttribute` do assembly `NetApiFlow`, o
mesmo valor de `<Version>` em `Directory.Build.props`. Usado pela sidebar do dashboard (tarefa
7.0c, item 4).

`tailSamplingEnabled`/`successSampleRate` (Fase 15, D-82) espelham
`NetApiFlowOptions.TailSampling`, para o dashboard avisar quando o histórico persistido não é
100% do tráfego real.

`openApiDocumentPath` (Fase 29, D-111) espelha `NetApiFlowOptions.OpenApiDocumentPath` (padrão
`/openapi/v1.json`), o caminho raiz-relativo de onde o host serve o próprio documento OpenAPI.
O painel "Testar um endpoint" lê daqui em vez de assumir o caminho padrão do `MapOpenApi()`,
porque a maioria dos projetos .NET existentes serve OpenAPI por Swashbuckle, em outro caminho.

`hostPathBase` (Fase 37, item 1 do roadmap de valor) é `HttpContext.Request.PathBase` lido no
momento da própria chamada a `/config`, string vazia quando o host não usa `PathBase` nenhum.
Existe porque `<base href>` do dashboard combina `{PathBase}{DashboardPath}`, e o painel "Testar
um endpoint" monta a URL do endpoint do usuário a partir da raiz real do host
(`{origin}{PathBase}{caminho do OpenAPI}`), nunca a partir do `<base href>` combinado, porque o
caminho do OpenAPI do usuário já é relativo à raiz do próprio host, não ao caminho onde o
dashboard foi montado.

### POST `/deploys`

Acrescentada na Fase 28 (D-60): cria um marco de deploy. Corpo `{ "label": string }`, `400` sem
`label` (vazio ou só espaço). `timestamp` sempre `DateTime.UtcNow` gerado no servidor, nunca
recebido do cliente, para um relógio de cliente divergente não contaminar a posição do marco na
timeline. `201` com o `DeployAnnotation` criado no corpo.

### GET `/deploys`

`from`/`to` obrigatórios na query string (`400` se faltar um), janela de busca por
`DeployAnnotation.Timestamp`, inclusive dos dois lados. Usado por `/history` para popular os
marcos intercalados com as execuções.

---

Autorização vale para as quinze rotas acima, sem exceção (ADR-0006): fora de
`IHostEnvironment.IsDevelopment()`, sem `RequireAuthorization` nem
`IDashboardAuthorizationFilter` configurado, toda rota responde `404`. Com um dos dois
configurado e o usuário sem a claim exigida, `403` (risco R-09, mitigação na Fase 5).
