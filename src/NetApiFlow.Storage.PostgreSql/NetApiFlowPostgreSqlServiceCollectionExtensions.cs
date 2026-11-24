using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NetApiFlow.Storage.PostgreSql;

/// <summary>Registers <see cref="ITraceStore"/> backed by PostgreSQL.</summary>
public static class NetApiFlowPostgreSqlServiceCollectionExtensions
{
    private const string MissingConnectionStringMessage =
        "NetApiFlow: the PostgreSQL connection string is null or empty. Pass the connection string of the database where NetApiFlow keeps its traces, for example builder.Configuration.GetConnectionString(\"NetApiFlow\").";

    private const string LoggerCategory = "NetApiFlow.Storage.PostgreSql";

    /// <summary>
    /// Replaces the default in-memory <see cref="ITraceStore"/> and <see cref="IDeployAnnotationStore"/>
    /// with ones backed by PostgreSQL. Call after <c>AddNetApiFlow(...)</c>, since this overrides their
    /// registration.
    /// </summary>
    public static IServiceCollection AddNetApiFlowPostgreSqlStorage(
        this IServiceCollection services, string connectionString, Action<PostgreSqlStorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ThrowIfMissing(connectionString);

        var options = new PostgreSqlStorageOptions();
        configure?.Invoke(options);

        services.Replace(ServiceDescriptor.Singleton<ITraceStore>(
            provider => new PostgreSqlTraceStore(connectionString, options, LoggerFor(provider))));
        services.Replace(ServiceDescriptor.Singleton<IDeployAnnotationStore>(
            provider => new PostgreSqlDeployAnnotationStore(connectionString, options, LoggerFor(provider))));

        return services;
    }

    private static ILogger LoggerFor(IServiceProvider provider) =>
        provider.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategory) ?? NullLogger.Instance;

    private static void ThrowIfMissing(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw connectionString is null
                ? new ArgumentNullException(nameof(connectionString), MissingConnectionStringMessage)
                : new ArgumentException(MissingConnectionStringMessage, nameof(connectionString));
        }
    }
}
