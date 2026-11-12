namespace NetApiFlow;

/// <summary>A single <c>ILogger</c> call correlated to the span active when it happened (ADR-0017).
/// Never populates <c>FlowSpan.Tags</c>: a separate field, its own opt-in, its own sanitization.</summary>
public sealed record LogEntry
{
    /// <summary>UTC timestamp when the log call happened.</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>Name of the <see cref="Microsoft.Extensions.Logging.LogLevel"/> the call was made at.</summary>
    public required string Level { get; init; }

    /// <summary>Sanitized, truncated log message.</summary>
    public required string Message { get; init; }

    /// <summary>Type name of the exception passed to the log call, when applicable.</summary>
    public string? ExceptionType { get; init; }

    /// <summary>Sanitized, truncated message of the exception passed to the log call, when applicable.</summary>
    public string? ExceptionMessage { get; init; }
}
