using System.Diagnostics;

namespace NetApiFlow.Instrumentation;

/// <summary>The single <see cref="ActivitySource"/> used for every span NetApiFlow records.</summary>
internal static class FlowActivitySource
{
    public const string Name = "NetApiFlow";

    public static readonly ActivitySource Source = new(Name);

    private static int _registeredListeners;
    private static int _enabledListeners;

    public static bool EveryRegisteredListenerIsDisabled =>
        Volatile.Read(ref _registeredListeners) > 0 && Volatile.Read(ref _enabledListeners) == 0;

    public static void RegisterListener(bool enabled)
    {
        Interlocked.Increment(ref _registeredListeners);
        if (enabled)
        {
            Interlocked.Increment(ref _enabledListeners);
        }
    }

    public static void UnregisterListener(bool enabled)
    {
        Interlocked.Decrement(ref _registeredListeners);
        if (enabled)
        {
            Interlocked.Decrement(ref _enabledListeners);
        }
    }
}
