using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NetApiFlow.Storage.SqlServer;

/// <summary>Registers <see cref="ITraceStore"/> backed by SQL Server.</summary>
public static class NetApiFlowSqlServerServiceCollectionExtensions
{
    private const string LoggerCategory = "NetApiFlow.Storage.SqlServer";

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
            provider => new SqlServerTraceStore(connectionString, options, LoggerFor(provider))));
        services.Replace(ServiceDescriptor.Singleton<IDeployAnnotationStore>(
            provider => new SqlServerDeployAnnotationStore(connectionString, options, LoggerFor(provider))));

        return services;
    }

    private static ILogger LoggerFor(IServiceProvider provider) =>
        provider.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategory) ?? NullLogger.Instance;
}
