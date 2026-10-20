using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetApiFlow.Dashboard.Contracts;
using NetApiFlow.Dashboard.Json;
using NetApiFlow.Dashboard.Sse;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using NetApiFlow.Routing;

namespace NetApiFlow.Dashboard.Endpoints;

/// <summary>Maps the eleven REST routes of the frozen dashboard contract (<c>docs/plano/contratos.md</c>, seção 3).</summary>
internal static class DashboardApiEndpoints
{
    public static IEndpointRouteBuilder MapNetApiFlowDashboardApi(this IEndpointRouteBuilder endpoints, string basePath = "/flow")
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<NetApiFlowOptions>>().Value;
        options.DashboardBasePath = basePath;

        var group = endpoints.MapGroup(basePath + "/api");
        group.AddEndpointFilter(DashboardAccessGate.EnforceAsync);

        group.MapGet("/traces", GetTraces);
        group.MapGet("/traces/live", GetLiveTraces);
        group.MapGet("/traces/{traceId}", GetTraceDetail);
        group.MapGet("/traces/{traceId}/spans/{spanId}", GetSpanDetail);
        group.MapDelete("/traces", DeleteTraces);
        group.MapGet("/blueprint", GetBlueprint);
        group.MapGet("/endpoints", GetEndpoints);
        group.MapGet("/metrics", GetMetrics);
        group.MapGet("/metrics/components", GetComponentMetrics);
        group.MapGet("/config", GetConfig);
        group.MapStream();

        return endpoints;
    }

    private static async Task<IResult> GetTraces(
        FlowStatus? status, string? method, string? route, string? tenant,
        DateTime? from, DateTime? to, long? minDurationMicros,
        int? page, int? pageSize,
        ITraceStore store, CancellationToken cancellationToken)
    {
        var resolvedPage = page ?? 1;
        var resolvedPageSize = pageSize ?? 50;

        if (resolvedPage < 1 || resolvedPageSize < 1 || resolvedPageSize > 200)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
        }

        var query = new TraceQuery
        {
            Status = status,
            Method = method,
            RouteTemplate = route,
            Tenant = tenant,
            From = from,
            To = to,
            MinDurationMicros = minDurationMicros,
            Page = resolvedPage,
            PageSize = resolvedPageSize,
        };

        var result = await store.QueryAsync(query, cancellationToken);
        return Results.Json(result, DashboardJsonOptions.Rest);
    }

    private static IResult GetLiveTraces(FlowRecorder recorder)
    {
        var items = recorder.GetActiveTraces()
            .Select(active => new FlowTraceSummary
            {
                TraceId = active.TraceId,
                Method = active.Method,
                RouteTemplate = active.RouteTemplate,
                HttpStatus = null,
                Status = FlowStatus.Running,
                StartedAt = active.StartedAtUtc,
                DurationMicros = null,
                Truncated = active.Truncated,
            })
            .ToList();

        return Results.Json(items, DashboardJsonOptions.Rest);
    }

    private static async Task<IResult> GetTraceDetail(string traceId, FlowRecorder recorder, ITraceStore store, CancellationToken cancellationToken)
    {
        var active = recorder.GetActiveTrace(traceId);
        if (active is not null)
        {
            return Results.Json(TraceDetailDto.From(active), DashboardJsonOptions.Rest);
        }

        var recent = recorder.GetRecentlyCompleted(traceId);
        if (recent is not null)
        {
            return Results.Json(TraceDetailDto.From(recent), DashboardJsonOptions.Rest);
        }

        var stored = await store.GetAsync(traceId, cancellationToken);
        return stored is null ? Results.NotFound() : Results.Json(TraceDetailDto.From(stored), DashboardJsonOptions.Rest);
    }

    private static async Task<IResult> GetSpanDetail(string traceId, string spanId, FlowRecorder recorder, ITraceStore store, CancellationToken cancellationToken)
    {
        var spans = recorder.GetActiveTrace(traceId)?.Spans
            ?? recorder.GetRecentlyCompleted(traceId)?.Spans;

        if (spans is null)
        {
            var stored = await store.GetAsync(traceId, cancellationToken);
            if (stored is null)
            {
                return Results.NotFound();
            }

            spans = stored.Spans;
        }

        var span = spans.FirstOrDefault(s => s.SpanId == spanId);
        if (span is null)
        {
            return Results.NotFound();
        }

        var ancestors = new List<FlowSpan>();
        var parentId = span.ParentSpanId;
        while (parentId is not null)
        {
            var parent = spans.FirstOrDefault(s => s.SpanId == parentId);
            if (parent is null)
            {
                break;
            }

            ancestors.Add(parent);
            parentId = parent.ParentSpanId;
        }

        ancestors.Reverse();

        var children = spans.Where(s => s.ParentSpanId == span.SpanId).ToList();

        var componentStats = await store.GetComponentStatsAsync(new StatsQuery(), cancellationToken);
        var componentStat = componentStats.FirstOrDefault(s => s.Component == span.Component && s.Operation == span.Operation);
        var median = componentStat?.MedianDurationMicros;
        var delta = median is not null && span.DurationMicros is not null ? span.DurationMicros - median : null;

        var response = new SpanDetailResponse
        {
            Span = SpanDto.From(span),
            Ancestors = [.. ancestors.Select(SpanRefDto.From)],
            Children = [.. children.Select(SpanRefDto.From)],
            MedianDurationMicros = median,
            DeltaFromMedianMicros = delta,
        };

        return Results.Json(response, DashboardJsonOptions.Rest);
    }

    private static async Task<IResult> DeleteTraces(ITraceStore store, FlowRecorder recorder, CancellationToken cancellationToken)
    {
        recorder.ClearActiveTraces();
        await store.ApplyRetentionAsync(new RetentionPolicy { MaxTraces = 0, MaxAge = TimeSpan.Zero }, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetBlueprint(string? method, string? route, ITraceStore store, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(route))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
        }

        var detail = await store.GetLastSuccessfulAsync(method, route, cancellationToken);
        return detail is null ? Results.NoContent() : Results.Json(TraceDetailDto.From(detail), DashboardJsonOptions.Rest);
    }

    private static IResult GetEndpoints(EndpointDataSource endpointDataSource, IOptions<NetApiFlowOptions> options)
    {
        var policy = new TracingRoutePolicy(options.Value);

        var descriptors = endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is not null)
            .Select(e => (
                Methods: e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? (IReadOnlyList<string>)[],
                RouteTemplate: "/" + e.RoutePattern.RawText!.TrimStart('/'),
                DisplayName: e.DisplayName ?? e.RoutePattern.RawText!))
            .Where(e => e.Methods.Count > 0 && policy.ShouldTrace(e.RouteTemplate))
            .SelectMany(e => e.Methods.Select(method => new EndpointDescriptor
            {
                Method = method,
                RouteTemplate = e.RouteTemplate,
                DisplayName = e.DisplayName,
            }))
            .OrderBy(e => e.RouteTemplate, StringComparer.Ordinal)
            .ThenBy(e => e.Method, StringComparer.Ordinal)
            .ToList();

        return Results.Json(descriptors, DashboardJsonOptions.Rest);
    }

    private static async Task<IResult> GetMetrics(DateTime? from, DateTime? to, ITraceStore store, CancellationToken cancellationToken)
    {
        var stats = await store.GetTraceStatsAsync(new StatsQuery { From = from, To = to }, cancellationToken);
        return Results.Json(MetricsSummary.From(stats), DashboardJsonOptions.Rest);
    }

    private static async Task<IResult> GetComponentMetrics(DateTime? from, DateTime? to, ITraceStore store, CancellationToken cancellationToken)
    {
        var stats = await store.GetComponentStatsAsync(new StatsQuery { From = from, To = to }, cancellationToken);
        var ordered = stats.OrderByDescending(s => s.ExecutionCount).ToList();
        return Results.Json(ordered, DashboardJsonOptions.Rest);
    }

    private static IResult GetConfig(IOptions<NetApiFlowOptions> options)
    {
        var config = new DashboardConfig
        {
            SlowThresholdMicros = (long)options.Value.SlowThreshold.TotalMicroseconds,
        };

        return Results.Json(config, DashboardJsonOptions.Rest);
    }
}
