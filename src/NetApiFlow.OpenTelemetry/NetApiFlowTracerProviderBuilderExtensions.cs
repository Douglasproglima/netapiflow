using OpenTelemetry.Trace;

namespace NetApiFlow.OpenTelemetry;

/// <summary>Adds NetApiFlow to the tracing an app already configures.</summary>
public static class NetApiFlowTracerProviderBuilderExtensions
{
    private const string NetApiFlowSourceName = "NetApiFlow";

    /// <summary>
    /// Adds the <c>"NetApiFlow"</c> <see cref="System.Diagnostics.ActivitySource"/> to this tracer provider, and
    /// nothing else: no exporter and no other source. The steps of NetApiFlow then go to the exporters the app
    /// already has. Use it when the app configures OpenTelemetry itself.
    /// </summary>
    public static TracerProviderBuilder AddNetApiFlowSource(this TracerProviderBuilder builder) =>
        builder.AddSource(NetApiFlowSourceName);
}
