namespace NetApiFlow.Storage.Tests;

internal sealed class ScriptedSchemaCheck<TConnection>(params bool[] outcomes)
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public Task<bool> Run(TConnection connection, CancellationToken cancellationToken)
    {
        var index = Interlocked.Increment(ref _calls) - 1;
        return Task.FromResult(outcomes[Math.Min(index, outcomes.Length - 1)]);
    }
}
