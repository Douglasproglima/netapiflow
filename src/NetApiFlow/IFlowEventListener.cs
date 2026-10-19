namespace NetApiFlow;

/// <summary>Receives events published by the recorder. A failure in one listener never affects the others.</summary>
public interface IFlowEventListener
{
    /// <summary>Handles a single published event.</summary>
    ValueTask OnEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken);
}
