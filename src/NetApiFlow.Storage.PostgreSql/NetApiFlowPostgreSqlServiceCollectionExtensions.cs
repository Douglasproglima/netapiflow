using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetApiFlow.Storage.PostgreSql;

/// <summary>Registers <see cref="ITraceStore"/> backed by PostgreSQL.</summary>
public static class NetApiFlowPostgreSqlServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the default in-memory <see cref="ITraceStore"/> and <see cref="IDeployAnnotationStore"/>
    /// with ones backed by PostgreSQL. Call after <c>AddNetApiFlow(...)</c>, since this overrides their
    /// registration.
    /// </summary>
    public static IServiceCollection AddNetApiFlowPostgreSqlStorage(
        this IServiceCollection services, string connectionString, Action<PostgreSqlStorageOptions>? configure = null)
    {
        var options = new PostgreSqlStorageOptions();
        configure?.Invoke(options);

        services.Replace(ServiceDescriptor.Singleton<ITraceStore>(
            _ => new PostgreSqlTraceStore(connectionString, options)));
        services.Replace(ServiceDescriptor.Singleton<IDeployAnnotationStore>(
            _ => new PostgreSqlDeployAnnotationStore(connectionString, options)));

        return services;
    }
}
