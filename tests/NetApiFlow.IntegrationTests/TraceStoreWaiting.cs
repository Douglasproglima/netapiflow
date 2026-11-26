using NetApiFlow.Domain;

namespace NetApiFlow.IntegrationTests;

internal static class TraceStoreWaiting
{
    private static readonly TimeSpan PersistenceDeadline = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(20);

    public static async Task<FlowTraceDetail> WaitForPersistedTraceAsync(this ITraceStore store, string traceId)
    {
        var deadline = DateTime.UtcNow + PersistenceDeadline;
        do
        {
            var persisted = await store.GetAsync(traceId, CancellationToken.None);
            if (persisted is not null)
            {
                return persisted;
            }

            await Task.Delay(PollingInterval);
        }
        while (DateTime.UtcNow < deadline);

        throw new InvalidOperationException($"Trace {traceId} was never persisted.");
    }
}
