using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace NetApiFlow.Marking;

internal sealed class FlowConventionRegistry(IServiceCollection services)
{
    public IServiceCollection Services => services;

    public List<AppliedConvention> Applied { get; } = [];

    public static FlowConventionRegistry For(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(FlowConventionRegistry))?.ImplementationInstance;
        if (existing is FlowConventionRegistry registry)
        {
            return registry;
        }

        var created = new FlowConventionRegistry(services);
        services.AddSingleton(created);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FlowConventionWarningHostedService>());
        return created;
    }

    internal sealed record AppliedConvention(FlowConventionOptions Options, Assembly? EntryAssembly)
    {
        public bool Matches(Type type) =>
            FlowConventions.ComponentTypeOf(type) is not null
            && Options.Includes(type, EntryAssembly)
            && !NetApiFlowOwnTypes.Is(type);
    }
}
