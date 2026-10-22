namespace NetApiFlow.Dashboard.Contracts;

/// <summary>Wire shape of <c>GET /traces/{traceId}/spans/{spanId}</c>.</summary>
internal sealed record SpanDetailResponse
{
    public required SpanDto Span { get; init; }

    public required IReadOnlyList<SpanRefDto> Ancestors { get; init; }

    public required IReadOnlyList<SpanRefDto> Children { get; init; }

    public long? MedianDurationMicros { get; init; }

    public long? DeltaFromMedianMicros { get; init; }
}
