using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace NetApiFlow.Configuration;

/// <summary>Registers <see cref="NetApiFlowOptions"/> binding and validation. Used internally by <c>AddNetApiFlow</c>.</summary>
internal static class ServiceCollectionOptionsExtensions
{
    public static IServiceCollection AddNetApiFlowOptions(this IServiceCollection services)
    {
        services.AddSingleton<IConfigureOptions<NetApiFlowOptions>, NetApiFlowOptionsSetup>();
        services.AddSingleton<IValidateOptions<NetApiFlowOptions>, NetApiFlowOptionsValidator>();
        services.AddOptions<NetApiFlowOptions>().ValidateOnStart();
        return services;
    }
}
