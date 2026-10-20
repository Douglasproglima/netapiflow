using System.Threading.Channels;
using NetApiFlow.Domain;

namespace NetApiFlow.Dashboard.Sse;

/// <summary>A connected SSE client's event queue. Disposing it unregisters the client from <see cref="SseBroadcaster"/>.</summary>
internal sealed class SseSubscription(ChannelReader<FlowEvent> reader, Action onDispose) : IDisposable
{
    public ChannelReader<FlowEvent> Reader { get; } = reader;

    public void Dispose() => onDispose();
}
