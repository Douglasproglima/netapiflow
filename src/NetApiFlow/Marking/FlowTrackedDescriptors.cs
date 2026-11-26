using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace NetApiFlow.Marking;

internal static class FlowTrackedDescriptors
{
    private static readonly ConditionalWeakTable<ServiceDescriptor, object> Tracked = new();

    private static readonly object Mark = new();

    public static ServiceDescriptor Remember(ServiceDescriptor descriptor)
    {
        Tracked.TryAdd(descriptor, Mark);
        return descriptor;
    }

    public static bool Contains(ServiceDescriptor descriptor) => Tracked.TryGetValue(descriptor, out _);
}
