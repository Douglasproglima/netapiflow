namespace NetApiFlow.Testing;

/// <summary>
/// Gets from an HTTP response a test just received to the trace NetApiFlow recorded for it,
/// without the test repeating the header read and the null check every time.
/// </summary>
public static class FlowTestingHttpResponseExtensions
{
    private const string TraceIdHeader = "X-NetApiFlow-TraceId";

    /// <summary>
    /// Reads <c>X-NetApiFlow-TraceId</c> off <paramref name="response"/> and returns the trace
    /// recorded for it.
    /// </summary>
    /// <exception cref="FlowAssertionException">
    /// The header is absent, which means the request was never tracked, or the trace is no longer
    /// in the in-memory buffer of recently completed traces.
    /// </exception>
    public static FlowTraceDetail GetFlowTrace(this HttpResponseMessage response, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(services);

        if (!response.Headers.TryGetValues(TraceIdHeader, out var values) ||
            values.FirstOrDefault() is not { Length: > 0 } traceId)
        {
            throw new FlowAssertionException(
                $"The response carries no {TraceIdHeader} header, so NetApiFlow did not track this request. " +
                "Check that AddNetApiFlow is registered and that the route is not excluded by IncludePaths/ExcludePaths.");
        }

        return NetApiFlow.GetRecentTrace(services, traceId)
            ?? throw new FlowAssertionException(
                $"No trace found for {traceId}. It either aged out of the in-memory buffer of recently " +
                "completed traces, or tracing is disabled on this host.");
    }
}
