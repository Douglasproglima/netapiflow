using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NetApiFlow.Domain;

namespace NetApiFlow.Dashboard.Sse;

/// <summary>
/// Maps <c>GET /stream</c>. The event-writing loop is isolated with <c>#if NET10_0_OR_GREATER</c>:
/// net10.0 uses <c>TypedResults.ServerSentEvents</c>; net8.0 writes
/// <c>text/event-stream</c> frames by hand. Everything else (client limit, replay, heartbeat
/// cadence) is shared.
/// </summary>
internal static class DashboardSseEndpoint
{
    private const string LastEventIdHeader = "Last-Event-ID";
    private const string ManualReconnectQueryParameter = "lastEventId";

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    public static void MapStream(this RouteGroupBuilder group) => group.MapGet("/stream", Handle);

    private static IResult Handle(HttpContext context, SseBroadcaster broadcaster)
    {
        var lastEventId = ParseLastEventId(context.Request.Headers[LastEventIdHeader])
            ?? ParseLastEventId(context.Request.Query[ManualReconnectQueryParameter]);
        var subscription = broadcaster.TrySubscribe(lastEventId);

        if (subscription is null)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        FlushEveryEventImmediately(context);
        return WriteStream(context, subscription);
    }

    private static void FlushEveryEventImmediately(HttpContext context) =>
        context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();

    private static long? ParseLastEventId(Microsoft.Extensions.Primitives.StringValues header) =>
        long.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static async Task<bool> TryWaitForHeartbeatAsync(PeriodicTimer heartbeat, CancellationToken cancellationToken)
    {
        try
        {
            return await heartbeat.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<bool> TryWaitToReadAsync(System.Threading.Channels.ChannelReader<FlowEvent> reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.WaitToReadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

#if NET10_0_OR_GREATER
    private static readonly System.Net.ServerSentEvents.SseItem<string> ConnectionConfirmation = new("{}", "ping");

    private static Microsoft.AspNetCore.Http.HttpResults.ServerSentEventsResult<string> WriteStream(HttpContext context, SseSubscription subscription) =>
        TypedResults.ServerSentEvents(StreamEventsAsync(subscription, context.RequestAborted));

    private static async IAsyncEnumerable<System.Net.ServerSentEvents.SseItem<string>> StreamEventsAsync(
        SseSubscription subscription,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var _ = subscription;
        using var heartbeat = new PeriodicTimer(HeartbeatInterval);
        var reader = subscription.Reader;

        yield return ConnectionConfirmation;

        var pendingHeartbeat = TryWaitForHeartbeatAsync(heartbeat, cancellationToken);

        while (true)
        {
            var readTask = TryWaitToReadAsync(reader, cancellationToken);
            var completed = await Task.WhenAny(readTask, pendingHeartbeat);

            if (completed == pendingHeartbeat)
            {
                if (!await pendingHeartbeat)
                {
                    yield break;
                }

                yield return new System.Net.ServerSentEvents.SseItem<string>(string.Empty, "ping");
                pendingHeartbeat = TryWaitForHeartbeatAsync(heartbeat, cancellationToken);
                continue;
            }

            if (!await readTask)
            {
                yield break;
            }

            while (reader.TryRead(out var flowEvent))
            {
                yield return new System.Net.ServerSentEvents.SseItem<string>(SseEventFormatter.SerializeData(flowEvent), SseEventFormatter.EventName(flowEvent))
                {
                    EventId = flowEvent.EventId.ToString(CultureInfo.InvariantCulture),
                };
            }
        }
    }
#else
    private static ManualSseResult WriteStream(HttpContext context, SseSubscription subscription) =>
        new(subscription);

    private sealed class ManualSseResult(SseSubscription subscription) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            using var _ = subscription;

            var response = context.Response;
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers["X-Accel-Buffering"] = "no";

            var cancellationToken = context.RequestAborted;
            var reader = subscription.Reader;
            using var heartbeat = new PeriodicTimer(HeartbeatInterval);

            try
            {
                await WriteConnectionConfirmationAsync(response, cancellationToken);

                var pendingHeartbeat = TryWaitForHeartbeatAsync(heartbeat, cancellationToken);

                while (true)
                {
                    var readTask = TryWaitToReadAsync(reader, cancellationToken);
                    var completed = await Task.WhenAny(readTask, pendingHeartbeat);

                    if (completed == pendingHeartbeat)
                    {
                        if (!await pendingHeartbeat)
                        {
                            return;
                        }

                        await response.WriteAsync(": ping\n\n", cancellationToken);
                        await response.Body.FlushAsync(cancellationToken);
                        pendingHeartbeat = TryWaitForHeartbeatAsync(heartbeat, cancellationToken);
                        continue;
                    }

                    if (!await readTask)
                    {
                        return;
                    }

                    while (reader.TryRead(out var flowEvent))
                    {
                        await WriteEventAsync(response, flowEvent, cancellationToken);
                    }
                }
            }
            catch (Exception) when (ClientHasDisconnected(cancellationToken))
            {
                return;
            }
        }

        private static bool ClientHasDisconnected(CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested;

        private static async Task WriteConnectionConfirmationAsync(HttpResponse response, CancellationToken cancellationToken)
        {
            await response.WriteAsync("event: ping\ndata: {}\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }

        private static async Task WriteEventAsync(HttpResponse response, FlowEvent flowEvent, CancellationToken cancellationToken)
        {
            await response.WriteAsync($"id: {flowEvent.EventId.ToString(CultureInfo.InvariantCulture)}\n", cancellationToken);
            await response.WriteAsync($"event: {SseEventFormatter.EventName(flowEvent)}\n", cancellationToken);
            await response.WriteAsync($"data: {SseEventFormatter.SerializeData(flowEvent)}\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
    }
#endif
}
