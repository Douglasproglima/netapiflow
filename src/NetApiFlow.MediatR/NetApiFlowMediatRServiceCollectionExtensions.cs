using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetApiFlow.MediatR;

/// <summary>Registers the MediatR pipeline behavior that tracks every handler as a <see cref="ComponentType.UseCase"/> step.</summary>
public static class NetApiFlowMediatRServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="FlowTrackingBehavior{TRequest, TResponse}"/> to the MediatR pipeline. Call
    /// this in addition to MediatR's own <c>AddMediatR(...)</c>, which registers the handlers.
    /// </summary>
    public static IServiceCollection AddNetApiFlowMediatR(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(global::MediatR.IPipelineBehavior<,>), typeof(FlowTrackingBehavior<,>)));
        return services;
    }
}
