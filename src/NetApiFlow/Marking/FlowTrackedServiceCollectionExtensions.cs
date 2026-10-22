using System.Reflection;
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

    /// <summary>
    /// Decorates every already-registered interface whose implementation type matches
    /// <paramref name="predicate"/> with a tracking proxy, preserving the original lifetime.
    /// Only <see cref="ServiceDescriptor"/> entries with a known implementation type (registered by
    /// type or by instance) can be matched; factory-based registrations are left untouched, since
    /// their concrete type is not known without invoking the factory.
    /// </summary>
    public static IServiceCollection DecorateFlowTracked(
        this IServiceCollection services,
        Func<Type, bool> predicate,
        ComponentType componentType = ComponentType.Service)
    {
        var createProxyMethod = typeof(FlowInterceptorProxy)
            .GetMethod(nameof(FlowInterceptorProxy.Create), BindingFlags.Public | BindingFlags.Static)!;

        var toDecorate = services
            .Select(descriptor => (descriptor, implementationType: GetImplementationType(descriptor)))
            .Where(entry => entry.descriptor.ServiceType.IsInterface
                            && entry.implementationType is not null
                            && predicate(entry.implementationType))
            .ToList();

        foreach (var (original, implementationType) in toDecorate)
        {
            var interfaceType = original.ServiceType;
            var index = services.IndexOf(original);

            services[index] = new ServiceDescriptor(
                interfaceType,
                sp =>
                {
                    var target = CreateOriginalInstance(original, sp);
                    return createProxyMethod.MakeGenericMethod(interfaceType, implementationType!).Invoke(null, [target, componentType])!;
                },
                original.Lifetime);
        }

        return services;
    }

    private static Type? GetImplementationType(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationType is not null)
        {
            return descriptor.ImplementationType;
        }

        return descriptor.ImplementationInstance?.GetType();
    }

    private static object CreateOriginalInstance(ServiceDescriptor descriptor, IServiceProvider provider)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return descriptor.ImplementationFactory(provider);
        }

        return ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
    }
}
