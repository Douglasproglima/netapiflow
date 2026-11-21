namespace NetApiFlow.Storage;

internal sealed record PersistenceRetryInterval(TimeSpan Value)
{
    public static PersistenceRetryInterval Default { get; } = new(TimeSpan.FromSeconds(30));
}
