using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace NetApiFlow.OpenTelemetry;

/// <summary>Registers the OpenTelemetry SDK to export NetApiFlow traces to an OTLP backend.</summary>
public static class NetApiFlowOpenTelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <c>TracerProvider</c> observing the <c>"NetApiFlow"</c> <see cref="System.Diagnostics.ActivitySource"/>
    /// and exports it via OTLP. The OTel SDK samples independently of
    /// <c>FlowActivityListener.Sample()</c>, so exported volume can exceed what the dashboard shows.
    /// </summary>
    public static IServiceCollection AddNetApiFlowOtlpExport(
        this IServiceCollection services,
        Action<NetApiFlowOtlpExportOptions>? configure = null)
    {
        var options = new NetApiFlowOtlpExportOptions();
        configure?.Invoke(options);

        services.AddOpenTelemetry().WithTracing(builder =>
        {
            builder.AddSource("NetApiFlow");

            if (options.IncludeHttpClientSource)
            {
                builder.AddSource("System.Net.Http");
            }

            builder.AddOtlpExporter(exporterOptions =>
            {
                options.ConfigureOtlpExporter?.Invoke(exporterOptions);
            });
        });

        return services;
    }
}
