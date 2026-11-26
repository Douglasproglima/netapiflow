using OpenTelemetry.Exporter;

namespace NetApiFlow.OpenTelemetry;

/// <summary>Options for <see cref="NetApiFlowOpenTelemetryServiceCollectionExtensions.AddNetApiFlowOtlpExport"/>.</summary>
public sealed class NetApiFlowOtlpExportOptions
{
    /// <summary>
    /// Also observes the native <c>"System.Net.Http"</c> <see cref="System.Diagnostics.ActivitySource"/>, mirroring
    /// <c>NetApiFlowOptions.ExternalSources</c>, so the exported trace has the same outbound HTTP spans
    /// the dashboard shows. That native <c>Activity</c> never passes through NetApiFlow's own URL sanitizer (which
    /// only sanitizes <c>FlowSpan</c>), so what reaches the backend is what the runtime wrote. With a call to
    /// <c>http://host/target?token=SECRET#fragment</c>, the span carried <c>url.full</c> as <c>http://host/target?*</c> on
    /// .NET 10 and as <c>http://host/target</c> on .NET 8, so neither exported the query string or the fragment.
    /// Another runtime version may behave differently. Default: <see langword="false"/>.
    /// </summary>
    public bool IncludeHttpClientSource { get; set; }

    /// <summary>Forwarded as-is to <c>AddOtlpExporter</c>. Default: the SDK's own default (http://localhost:4317).</summary>
    public Action<OtlpExporterOptions>? ConfigureOtlpExporter { get; set; }
}
