namespace NetApiFlow.Storage;

internal sealed record RetentionInterval(TimeSpan Value)
{
    public static RetentionInterval Default { get; } = new(TimeSpan.FromMinutes(5));
}
