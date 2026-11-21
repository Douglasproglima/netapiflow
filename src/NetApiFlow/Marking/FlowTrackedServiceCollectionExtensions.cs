using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace NetApiFlow.Marking;

/// <summary>Registers interfaces whose calls should become tracked steps automatically.</summary>
public static class FlowTrackedServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TImplementation"/> with <paramref name="lifetime"/>, and
    /// <typeparamref name="TInterface"/> resolving to a tracking proxy around it, with the same
    /// lifetime. The proxy is created by a service factory, not by registering the interface
    /// separately, so DI does not create a second instance of the implementation.
    /// </summary>
    [RequiresDynamicCode("The tracking proxy is built with DispatchProxy, which emits a new type at run time and has no Native AOT equivalent. Under Native AOT, mark steps with NetApiFlow.Step instead, which publishes without warnings.")]
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
    [RequiresDynamicCode("The tracking proxy is built with DispatchProxy and closed over implementation types only known at run time, neither of which Native AOT can compile ahead of time. Under Native AOT, mark steps with NetApiFlow.Step instead, which publishes without warnings.")]
    [RequiresUnreferencedCode("Registered implementation types are matched by reflection, so trimming can remove a type that would otherwise be decorated, silently dropping its steps.")]
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
            var createProxy = createProxyMethod.MakeGenericMethod(interfaceType, implementationType!);
            Func<IServiceProvider, object> resolveOriginal = original.ImplementationType is null
                ? _ => original.ImplementationInstance!
                : RegisterOriginalOwnedByTheContainer(services, original);

            services[index] = new ServiceDescriptor(
                interfaceType,
                sp => createProxy.Invoke(null, [resolveOriginal(sp), componentType])!,
                original.Lifetime);
        }

        return services;
    }

    private static Func<IServiceProvider, object> RegisterOriginalOwnedByTheContainer(IServiceCollection services, ServiceDescriptor original)
    {
        var implementationType = original.ImplementationType!;
        var key = new object();
        services.Add(ServiceDescriptor.DescribeKeyed(implementationType, key, implementationType, original.Lifetime));
        return sp => sp.GetRequiredKeyedService(implementationType, key);
    }

    private static Type? GetImplementationType(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationType is not null)
        {
            return descriptor.ImplementationType;
        }

        return descriptor.ImplementationInstance?.GetType();
    }
}
