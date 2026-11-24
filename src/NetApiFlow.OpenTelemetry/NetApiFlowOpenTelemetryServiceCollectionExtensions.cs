using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace NetApiFlow.OpenTelemetry;

/// <summary>Registers the OpenTelemetry SDK to export NetApiFlow traces to an OTLP backend.</summary>
public static class NetApiFlowOpenTelemetryServiceCollectionExtensions
{
    private const string HttpClientSourceName = "System.Net.Http";

    /// <summary>
    /// Registers a <c>TracerProvider</c> observing the <c>"NetApiFlow"</c> <see cref="System.Diagnostics.ActivitySource"/>
    /// and exports it via OTLP. The OTel SDK samples independently of
    /// <c>FlowActivityListener.Sample()</c>, so exported volume can exceed what the dashboard shows.
    /// The exporter is added to the <c>TracerProvider</c> of the app, so in an app that already configures
    /// OpenTelemetry it also receives every source of the app. Use <c>AddNetApiFlowSource()</c> there instead.
    /// Calling it again adds no exporter, and the <paramref name="configure"/> of every call is applied, in
    /// the order of the calls.
    /// </summary>
    public static IServiceCollection AddNetApiFlowOtlpExport(
        this IServiceCollection services,
        Action<NetApiFlowOtlpExportOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var registration = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<NetApiFlowOtlpExportRegistration>()
            .FirstOrDefault();

        if (registration is null)
        {
            registration = new NetApiFlowOtlpExportRegistration();
            services.AddSingleton(registration);
            AddExport(services, registration.Options);
        }

        configure?.Invoke(registration.Options);
        return services;
    }

    private static void AddExport(IServiceCollection services, NetApiFlowOtlpExportOptions options) =>
        services.AddOpenTelemetry().WithTracing(builder =>
        {
            builder.AddNetApiFlowSource();
            ((IDeferredTracerProviderBuilder)builder).Configure((_, tracing) =>
            {
                if (options.IncludeHttpClientSource)
                {
                    tracing.AddSource(HttpClientSourceName);
                }
            });
            builder.AddOtlpExporter(exporterOptions => options.ConfigureOtlpExporter?.Invoke(exporterOptions));
        });

    private sealed class NetApiFlowOtlpExportRegistration
    {
        public NetApiFlowOtlpExportOptions Options { get; } = new();
    }
}
