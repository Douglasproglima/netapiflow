using NetApiFlow.Domain;
using NetApiFlow.Routing;

namespace NetApiFlow.Storage.Tests;

internal static class SchemaScriptTestData
{
    public static IEnumerable<object[]> RouteTemplates() =>
    [
        ["/api/orders/{id:int}"],
        ["/api/orders/{id}"],
        ["api/orders/{id:int}"],
        ["/api/orders/{id:int}/"],
        ["/api/orders/{id?}"],
        ["/api/orders/{id:int?}"],
        ["/files/{*path}"],
        ["/files/{**path}"],
        ["/a/{b?}/c/{d:guid}"],
        ["/api/items/{code:regex(^[A-Z]+$)}"],
        ["/health"],
        ["/"],
    ];

    public static string ExpectedRouteKey(string routeTemplate) =>
        RouteTemplateNormalizer.NormalizeForComparison(routeTemplate);

    public static FlowTrace Trace(string traceId, string routeTemplate) => new()
    {
        TraceId = traceId,
        Method = "GET",
        RouteTemplate = routeTemplate,
        HttpStatus = 200,
        Status = FlowStatus.Success,
        StartedAt = DateTime.UtcNow,
        EndedAt = DateTime.UtcNow.AddMicroseconds(1_000),
        DurationMicros = 1_000,
        SpanCount = 1,
        Truncated = false,
        ErrorMessage = null,
        Tags = new Dictionary<string, string>(),
    };

    public static FlowSpan Span(string traceId, string spanId) => new()
    {
        TraceId = traceId,
        SpanId = spanId,
        ParentSpanId = null,
        Sequence = 0,
        Component = "OrderRepository",
        Operation = "FindAsync",
        Type = ComponentType.Repository,
        StartedAt = DateTime.UtcNow,
        EndedAt = DateTime.UtcNow.AddMicroseconds(1_000),
        DurationMicros = 1_000,
        Status = FlowStatus.Success,
        ErrorType = null,
        ErrorMessage = null,
        IsErrorOrigin = false,
        Tags = new Dictionary<string, string>(),
        Logs = [],
    };
}
