using System.Collections.Concurrent;

public sealed class Gate
{
    public TaskCompletionSource Inside { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public static class Gates
{
    private static readonly ConcurrentDictionary<string, Gate> All = new();

    public static Gate Of(string name) => All.GetOrAdd(name, _ => new Gate());

    public static Gate Reset(string name) => All[name] = new Gate();
}
