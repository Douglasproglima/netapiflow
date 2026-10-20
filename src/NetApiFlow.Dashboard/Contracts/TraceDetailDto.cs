using NetApiFlow.Domain;
using NetApiFlow.Recording;

namespace NetApiFlow.Dashboard.Contracts;

/// <summary>Wire shape of <c>GET /traces/{traceId}</c> and <c>GET /blueprint</c>: a trace with its spans.</summary>
internal sealed record TraceDetailDto
{
    public required string TraceId { get; init; }

    public required string Method { get; init; }

    public required string RouteTemplate { get; init; }

    public int? HttpStatus { get; init; }

    public required FlowStatus Status { get; init; }

    public required DateTime StartedAt { get; init; }

    public DateTime? EndedAt { get; init; }

    public long? DurationMicros { get; init; }

    public required int SpanCount { get; init; }

    public required bool Truncated { get; init; }

    public string? ErrorMessage { get; init; }

    public required IReadOnlyDictionary<string, string> Tags { get; init; }

    public required IReadOnlyList<SpanDto> Spans { get; init; }

    public static TraceDetailDto From(FlowTraceDetail detail) => From(detail.Trace, detail.Spans);

    public static TraceDetailDto From(ActiveTrace activeTrace)
    {
        var spans = activeTrace.Spans;
        return new TraceDetailDto
        {
            TraceId = activeTrace.TraceId,
            Method = activeTrace.Method,
            RouteTemplate = activeTrace.RouteTemplate,
            HttpStatus = null,
            Status = FlowStatus.Running,
            StartedAt = activeTrace.StartedAtUtc,
            EndedAt = null,
            DurationMicros = null,
            SpanCount = spans.Count,
            Truncated = activeTrace.Truncated,
            ErrorMessage = null,
            Tags = EmptyTags,
            Spans = [.. spans.OrderBy(s => s.Sequence).Select(SpanDto.From)],
        };
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyTags = new Dictionary<string, string>();

    private static TraceDetailDto From(FlowTrace trace, IReadOnlyList<FlowSpan> spans) => new()
    {
        TraceId = trace.TraceId,
        Method = trace.Method,
        RouteTemplate = trace.RouteTemplate,
        HttpStatus = trace.HttpStatus,
        Status = trace.Status,
        StartedAt = trace.StartedAt,
        EndedAt = trace.EndedAt,
        DurationMicros = trace.DurationMicros,
        SpanCount = trace.SpanCount,
        Truncated = trace.Truncated,
        ErrorMessage = trace.ErrorMessage,
        Tags = trace.Tags,
        Spans = [.. spans.OrderBy(s => s.Sequence).Select(SpanDto.From)],
    };
}
