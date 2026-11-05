using System.Diagnostics;

namespace NetApiFlow.Instrumentation;

/// <summary>The single <see cref="ActivitySource"/> used for every span NetApiFlow records.</summary>
internal static class FlowActivitySource
{
    public const string Name = "NetApiFlow";

    public static readonly ActivitySource Source = new(Name);
}
