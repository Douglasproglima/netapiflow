using Microsoft.Extensions.DependencyInjection;

namespace NetApiFlow.EntityFrameworkCore;

/// <summary>Registers <see cref="FlowDbCommandInterceptor"/> for resolution via DI.</summary>
public static class NetApiFlowEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FlowDbCommandInterceptor"/> as a singleton. You still need to attach it
    /// to your <c>DbContext</c> explicitly, e.g. <c>optionsBuilder.AddInterceptors(interceptor)</c>,
    /// since EF Core has no global interceptor registration mechanism.
    /// </summary>
    public static IServiceCollection AddNetApiFlowEntityFrameworkCore(this IServiceCollection services)
    {
        services.AddSingleton<FlowDbCommandInterceptor>();
        return services;
    }
}
