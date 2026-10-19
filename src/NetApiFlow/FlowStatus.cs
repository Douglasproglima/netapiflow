namespace NetApiFlow;

/// <summary>Execution status of a <see cref="Domain.FlowTrace"/> or <see cref="Domain.FlowSpan"/>.</summary>
public enum FlowStatus
{
    /// <summary>Still executing.</summary>
    Running,

    /// <summary>Finished without an exception.</summary>
    Success,

    /// <summary>Finished with an exception, or a descendant span failed.</summary>
    Error,
}
