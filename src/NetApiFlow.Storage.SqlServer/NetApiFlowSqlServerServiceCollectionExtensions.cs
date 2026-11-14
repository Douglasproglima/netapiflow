using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetApiFlow.Storage.SqlServer;

/// <summary>Registers <see cref="ITraceStore"/> backed by SQL Server.</summary>
public static class NetApiFlowSqlServerServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the default in-memory <see cref="ITraceStore"/> and <see cref="IDeployAnnotationStore"/>
    /// with ones backed by SQL Server. Call after <c>AddNetApiFlow(...)</c>, since this overrides their
    /// registration.
    /// </summary>
    public static IServiceCollection AddNetApiFlowSqlServerStorage(
        this IServiceCollection services, string connectionString, Action<SqlServerStorageOptions>? configure = null)
    {
        var options = new SqlServerStorageOptions();
        configure?.Invoke(options);

        services.Replace(ServiceDescriptor.Singleton<ITraceStore>(
            _ => new SqlServerTraceStore(connectionString, options)));
        services.Replace(ServiceDescriptor.Singleton<IDeployAnnotationStore>(
            _ => new SqlServerDeployAnnotationStore(connectionString, options)));

        return services;
    }
}
