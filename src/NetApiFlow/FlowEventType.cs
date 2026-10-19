namespace NetApiFlow;

/// <summary>Kind of <see cref="FlowEvent"/> published by the recorder (see seção 6.5 do prompt).</summary>
public enum FlowEventType
{
    /// <summary>The middleware opened a trace.</summary>
    TraceStarted,

    /// <summary>An activity started.</summary>
    SpanStarted,

    /// <summary>An activity ended without an exception.</summary>
    SpanCompleted,

    /// <summary>An activity ended with an exception, or a descendant span failed.</summary>
    SpanFailed,

    /// <summary>The middleware closed a trace.</summary>
    TraceCompleted,

    /// <summary>The writer persisted a trace to the configured trace store.</summary>
    TracePersisted,
}
