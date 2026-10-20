using System.Collections.Concurrent;
using System.Threading.Channels;
using NetApiFlow.Domain;

namespace NetApiFlow.Dashboard.Sse;

/// <summary>
/// Fans out <see cref="FlowEvent"/>s to every connected <c>GET /stream</c> client, keeping a
/// circular replay buffer for <c>Last-Event-ID</c> reconnection (tarefa 3.6) and enforcing the
/// client and per-client queue limits of risco R-10 (tarefa 3.7).
/// </summary>
internal sealed class SseBroadcaster : IFlowEventListener
{
    /// <summary>Maximum number of SSE clients connected at the same time. The 51st connection gets 503.</summary>
    internal const int MaxClients = 50;

    /// <summary>Size of the circular replay buffer used to serve <c>Last-Event-ID</c> reconnects.</summary>
    internal const int ReplayBufferCapacity = 1000;

    /// <summary>Per-client bounded queue capacity. Oldest events are dropped once a slow client falls behind.</summary>
    internal const int PerClientQueueCapacity = 5000;

    private readonly ConcurrentQueue<FlowEvent> _replayBuffer = new();
    private int _replayBufferCount;
    private readonly ConcurrentDictionary<Guid, Channel<FlowEvent>> _clients = new();

    public int ClientCount => _clients.Count;

    public ValueTask OnEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken)
    {
        _replayBuffer.Enqueue(flowEvent);
        if (Interlocked.Increment(ref _replayBufferCount) > ReplayBufferCapacity && _replayBuffer.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _replayBufferCount);
        }

        foreach (var channel in _clients.Values)
        {
            channel.Writer.TryWrite(flowEvent);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Registers a new client, or returns <see langword="null"/> when <see cref="MaxClients"/> is already connected.
    /// Replay events are queued before the client is registered for live broadcast, so a reconnecting
    /// client can miss an event born in that narrow window but never receives one twice (tarefa 3.6).
    /// </summary>
    public SseSubscription? TrySubscribe(long? lastEventId)
    {
        if (_clients.Count >= MaxClients)
        {
            return null;
        }

        var channel = Channel.CreateBounded<FlowEvent>(new BoundedChannelOptions(PerClientQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        if (lastEventId is not null)
        {
            var replay = _replayBuffer.ToArray()
                .Where(e => e.EventId > lastEventId)
                .OrderBy(e => e.EventId);

            foreach (var e in replay)
            {
                channel.Writer.TryWrite(e);
            }
        }

        var id = Guid.NewGuid();
        if (!_clients.TryAdd(id, channel))
        {
            return null;
        }

        return new SseSubscription(channel.Reader, () => _clients.TryRemove(id, out _));
    }
}
