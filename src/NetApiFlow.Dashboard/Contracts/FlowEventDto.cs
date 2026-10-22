using NetApiFlow.Domain;

namespace NetApiFlow.Dashboard.Contracts;

/// <summary>Wire shape of an SSE event (<c>docs/plano/contratos.md</c>, seção 2). Exactly one of <see cref="Span"/>/<see cref="Trace"/> is present.</summary>
internal sealed record FlowEventDto
{
    public required long EventId { get; init; }

    public required FlowEventType Type { get; init; }

    public required string TraceId { get; init; }

    public required DateTime Timestamp { get; init; }

    public SpanDto? Span { get; init; }

    public FlowTrace? Trace { get; init; }

    public static FlowEventDto From(FlowEvent flowEvent) => new()
    {
        EventId = flowEvent.EventId,
        Type = flowEvent.Type,
        TraceId = flowEvent.TraceId,
        Timestamp = flowEvent.Timestamp,
        Span = flowEvent.Span is null ? null : SpanDto.From(flowEvent.Span),
        Trace = flowEvent.Trace,
    };
}
