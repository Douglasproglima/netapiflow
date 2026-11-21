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
        services.Add(FlowTrackedDescriptors.Remember(new ServiceDescriptor(
            typeof(TInterface),
            sp => FlowInterceptorProxy.Create<TInterface, TImplementation>(sp.GetRequiredService<TImplementation>(), componentType),
            lifetime)));

        return services;
    }

    /// <summary>
    /// Decorates every already-registered interface whose implementation type matches
    /// <paramref name="predicate"/> with a tracking proxy, preserving the original lifetime.
    /// Only <see cref="ServiceDescriptor"/> entries with a known implementation type (registered by
    /// type or by instance) can be matched; factory-based registrations are left untouched, since
    /// their concrete type is not known without invoking the factory. The original is registered
    /// with a private key, so the container owns and disposes it, and for that reason the
    /// container has to support keyed services.
    /// </summary>
    [RequiresDynamicCode("The tracking proxy is built with DispatchProxy and closed over implementation types only known at run time, neither of which Native AOT can compile ahead of time. Under Native AOT, mark steps with NetApiFlow.Step instead, which publishes without warnings.")]
    [RequiresUnreferencedCode("Registered implementation types are matched by reflection, so trimming can remove a type that would otherwise be decorated, silently dropping its steps.")]
    public static IServiceCollection DecorateFlowTracked(
        this IServiceCollection services,
        Func<Type, bool> predicate,
        ComponentType componentType = ComponentType.Service)
    {
        DecorateWhere(services, (_, implementationType) => predicate(implementationType), componentType);
        return services;
    }

    [RequiresDynamicCode("The tracking proxy is built with DispatchProxy at run time.")]
    [RequiresUnreferencedCode("Registered implementation types are matched by reflection.")]
    internal static List<ServiceDescriptor> DecorateWhere(
        IServiceCollection services,
        Func<ServiceDescriptor, Type, bool> predicate,
        ComponentType componentType)
    {
        var createProxyMethod = typeof(FlowInterceptorProxy)
            .GetMethod(nameof(FlowInterceptorProxy.Create), BindingFlags.Public | BindingFlags.Static)!;

        var toDecorate = services
            .Select(descriptor => (descriptor, implementationType: GetImplementationType(descriptor)))
            .Where(entry => entry.descriptor.ServiceType.IsInterface
                            && !entry.descriptor.ServiceType.ContainsGenericParameters
                            && entry.implementationType is not null
                            && !entry.implementationType.ContainsGenericParameters
                            && !NetApiFlowOwnTypes.Is(entry.implementationType)
                            && predicate(entry.descriptor, entry.implementationType))
            .ToList();

        var decorated = new List<ServiceDescriptor>(toDecorate.Count);
        foreach (var (original, implementationType) in toDecorate)
        {
            var interfaceType = original.ServiceType;
            var index = services.IndexOf(original);
            var createProxy = createProxyMethod.MakeGenericMethod(interfaceType, implementationType!);
            Func<IServiceProvider, object> resolveOriginal = original.ImplementationType is null
                ? _ => original.ImplementationInstance!
                : RegisterOriginalOwnedByTheContainer(services, original);

            var replacement = FlowTrackedDescriptors.Remember(new ServiceDescriptor(
                interfaceType,
                sp => createProxy.Invoke(null, [resolveOriginal(sp), componentType])!,
                original.Lifetime));
            services[index] = replacement;
            decorated.Add(replacement);
        }

        return decorated;
    }

    /// <summary>
    /// Tracks the application's own services, repositories and handlers by name: <c>*Service</c> as
    /// <see cref="ComponentType.Service"/>, <c>*Repository</c> as <see cref="ComponentType.Repository"/>,
    /// <c>*Handler</c> as <see cref="ComponentType.UseCase"/>. Only types of the entry assembly, or of
    /// the assemblies and namespaces named in <paramref name="configure"/>, are ever decorated, never a
    /// framework, third-party or NetApiFlow type. Call it after your own registrations: a registration
    /// made later is not decorated, and the host logs a warning naming it. A registration made by
    /// factory cannot be decorated, and the host logs a warning naming it too.
    /// </summary>
    [RequiresDynamicCode("The tracking proxy is built with DispatchProxy and closed over implementation types only known at run time, neither of which Native AOT can compile ahead of time. Under Native AOT, mark steps with NetApiFlow.Step instead, which publishes without warnings.")]
    [RequiresUnreferencedCode("Registered implementation types are matched by reflection, so trimming can remove a type that would otherwise be decorated, silently dropping its steps.")]
    public static IServiceCollection TrackFlowByConvention(
        this IServiceCollection services,
        Action<FlowConventionOptions>? configure = null) =>
        TrackFlowByConventionOf(services, configure, Assembly.GetEntryAssembly());

    [RequiresDynamicCode("The tracking proxy is built with DispatchProxy at run time.")]
    [RequiresUnreferencedCode("Registered implementation types are matched by reflection.")]
    internal static IServiceCollection TrackFlowByConventionOf(
        IServiceCollection services,
        Action<FlowConventionOptions>? configure,
        Assembly? entryAssembly)
    {
        var options = new FlowConventionOptions();
        configure?.Invoke(options);
        var applied = new FlowConventionRegistry.AppliedConvention(options, entryAssembly);
        FlowConventionRegistry.For(services).Applied.Add(applied);

        foreach (var component in FlowConventions.Components)
        {
            DecorateWhere(
                services,
                (descriptor, implementationType) =>
                    applied.Matches(implementationType)
                    && FlowConventions.ComponentTypeOf(implementationType) == component
                    && !FlowConventions.IsMediatRService(descriptor.ServiceType),
                component);
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

    private static Func<IServiceProvider, object> RegisterOriginalOwnedByTheContainer(IServiceCollection services, ServiceDescriptor original)
    {
        var implementationType = original.ImplementationType!;
        var key = new object();
        services.Add(ServiceDescriptor.DescribeKeyed(implementationType, key, implementationType, original.Lifetime));
        return sp => sp.GetRequiredKeyedService(implementationType, key);
    }
}
