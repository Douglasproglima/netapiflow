using NetApiFlow.Domain;

namespace NetApiFlow.Dashboard.Contracts;

/// <summary>Minimal reference to a span, used for ancestor/children lists in <c>GET /traces/{traceId}/spans/{spanId}</c>.</summary>
internal sealed record SpanRefDto
{
    public required string SpanId { get; init; }

    public required string Component { get; init; }

    public required string Operation { get; init; }

    public static SpanRefDto From(FlowSpan span) => new()
    {
        SpanId = span.SpanId,
        Component = span.Component,
        Operation = span.Operation,
    };
}
