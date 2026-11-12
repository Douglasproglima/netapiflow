using NetApiFlow.Domain;

namespace NetApiFlow.Dashboard.Contracts;

/// <summary>Wire shape of a <see cref="FlowSpan"/>, without the trace id it already belongs to.</summary>
internal sealed record SpanDto
{
    public required string SpanId { get; init; }

    public string? ParentSpanId { get; init; }

    public required int Sequence { get; init; }

    public required string Component { get; init; }

    public required string Operation { get; init; }

    public required ComponentType Type { get; init; }

    public required DateTime StartedAt { get; init; }

    public DateTime? EndedAt { get; init; }

    public long? DurationMicros { get; init; }

    public required FlowStatus Status { get; init; }

    public string? ErrorType { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsErrorOrigin { get; init; }

    public required IReadOnlyDictionary<string, string> Tags { get; init; }

    public required IReadOnlyList<LogEntry> Logs { get; init; }

    public static SpanDto From(FlowSpan span) => new()
    {
        SpanId = span.SpanId,
        ParentSpanId = span.ParentSpanId,
        Sequence = span.Sequence,
        Component = span.Component,
        Operation = span.Operation,
        Type = span.Type,
        StartedAt = span.StartedAt,
        EndedAt = span.EndedAt,
        DurationMicros = span.DurationMicros,
        Status = span.Status,
        ErrorType = span.ErrorType,
        ErrorMessage = span.ErrorMessage,
        IsErrorOrigin = span.IsErrorOrigin,
        Tags = span.Tags,
        Logs = span.Logs,
    };
}
