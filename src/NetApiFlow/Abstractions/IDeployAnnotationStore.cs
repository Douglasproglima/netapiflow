namespace NetApiFlow;

/// <summary>Persists and queries deploy annotations. Sibling of <see cref="ITraceStore"/>, implemented by the in-memory default store and by the optional storage packages.</summary>
public interface IDeployAnnotationStore
{
    /// <summary>Saves a deploy annotation.</summary>
    ValueTask SaveAsync(DeployAnnotation annotation, CancellationToken cancellationToken);

    /// <summary>Lists annotations with a <see cref="DeployAnnotation.Timestamp"/> within <paramref name="from"/> and <paramref name="until"/>, inclusive.</summary>
    ValueTask<IReadOnlyList<DeployAnnotation>> QueryAsync(DateTime from, DateTime until, CancellationToken cancellationToken);
}
