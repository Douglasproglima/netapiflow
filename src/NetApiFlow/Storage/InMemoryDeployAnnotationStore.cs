using System.Collections.Concurrent;

namespace NetApiFlow.Storage;

/// <summary>Default <see cref="IDeployAnnotationStore"/>: keeps every annotation in memory for the lifetime of the process.</summary>
internal sealed class InMemoryDeployAnnotationStore : IDeployAnnotationStore
{
    private readonly ConcurrentDictionary<string, DeployAnnotation> _annotations = new();

    public ValueTask SaveAsync(DeployAnnotation annotation, CancellationToken cancellationToken)
    {
        _annotations[annotation.Id] = annotation;
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<DeployAnnotation>> QueryAsync(DateTime from, DateTime until, CancellationToken cancellationToken)
    {
        var matching = _annotations.Values
            .Where(a => a.Timestamp >= from && a.Timestamp <= until)
            .OrderBy(a => a.Timestamp)
            .ToList();

        return ValueTask.FromResult<IReadOnlyList<DeployAnnotation>>(matching);
    }
}
