using Microsoft.Extensions.DependencyInjection;

namespace NetApiFlow.Marking;

/// <summary>Registers interfaces whose calls should become tracked steps automatically.</summary>
public static class FlowTrackedServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TImplementation"/> with <paramref name="lifetime"/>, and
    /// <typeparamref name="TInterface"/> resolving to a tracking proxy around it, with the same
    /// lifetime (risco R-02: the proxy is created by an <see cref="IServiceCollection"/> factory,
    /// not by registering the interface separately, so DI does not create a second lifetime scope
    /// for the underlying implementation).
    /// </summary>
    public static IServiceCollection AddFlowTracked<TInterface, TImplementation>(
        this IServiceCollection services,
        ComponentType componentType,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TInterface : class
        where TImplementation : class, TInterface
    {
        services.Add(new ServiceDescriptor(typeof(TImplementation), typeof(TImplementation), lifetime));
        services.Add(new ServiceDescriptor(
            typeof(TInterface),
            sp => FlowInterceptorProxy.Create<TInterface, TImplementation>(sp.GetRequiredService<TImplementation>(), componentType),
            lifetime));

        return services;
    }
}
