namespace NetApiFlow.PrePipelineInteropSpike;

internal static class TraceParentRequests
{
    public static string NewTraceId() => Guid.NewGuid().ToString("N");

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string traceId)
    {
        var spanId = Guid.NewGuid().ToString("N")[..16];
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("traceparent", $"00-{traceId}-{spanId}-01");
        return await client.SendAsync(request);
    }
}
