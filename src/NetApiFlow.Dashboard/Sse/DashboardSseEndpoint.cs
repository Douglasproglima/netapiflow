using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NetApiFlow.Domain;

namespace NetApiFlow.Dashboard.Sse;

/// <summary>
/// Maps <c>GET /stream</c>. The event-writing loop is isolated with <c>#if NET10_0_OR_GREATER</c>
/// (ADR-0003, risco R-04): net10.0 uses <c>TypedResults.ServerSentEvents</c>; net8.0 writes
/// <c>text/event-stream</c> frames by hand. Everything else (client limit, replay, heartbeat
/// cadence) is shared.
/// </summary>
internal static class DashboardSseEndpoint
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    public static void MapStream(this RouteGroupBuilder group) => group.MapGet("/stream", Handle);

    private static IResult Handle(HttpContext context, SseBroadcaster broadcaster)
    {
        var lastEventId = ParseLastEventId(context.Request.Headers["Last-Event-ID"]);
        var subscription = broadcaster.TrySubscribe(lastEventId);

        if (subscription is null)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        // Each event must reach the client as soon as it is written, not once some internal
        // buffer fills up (that would defeat the purpose of a live stream).
        context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
        return WriteStream(context, subscription);
    }

    private static long? ParseLastEventId(Microsoft.Extensions.Primitives.StringValues header) =>
        long.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

#if NET10_0_OR_GREATER
    private static Microsoft.AspNetCore.Http.HttpResults.ServerSentEventsResult<string> WriteStream(HttpContext context, SseSubscription subscription) =>
        TypedResults.ServerSentEvents(StreamEventsAsync(subscription, context.RequestAborted));

    private static async IAsyncEnumerable<System.Net.ServerSentEvents.SseItem<string>> StreamEventsAsync(
        SseSubscription subscription,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var _ = subscription;
        using var heartbeat = new PeriodicTimer(HeartbeatInterval);
        var reader = subscription.Reader;

        // Confirms the connection immediately: without this, nothing is written to the client
        // until the first real event or the first heartbeat tick, whichever comes first, and the
        // response headers themselves do not flush until that first write happens.
        yield return new System.Net.ServerSentEvents.SseItem<string>("{}", "ping");

        // PeriodicTimer.WaitForNextTickAsync does not support a second overlapping call while a
        // previous one is still pending, so the pending call is only ever replaced once it has
        // actually completed, never recreated every loop iteration.
        var heartbeatTask = heartbeat.WaitForNextTickAsync(cancellationToken).AsTask();

        while (true)
        {
            var readTask = reader.WaitToReadAsync(cancellationToken).AsTask();
            var completed = await Task.WhenAny(readTask, heartbeatTask);

            if (completed == heartbeatTask)
            {
                if (!heartbeatTask.Result)
                {
                    yield break;
                }

                yield return new System.Net.ServerSentEvents.SseItem<string>(string.Empty, "ping");
                heartbeatTask = heartbeat.WaitForNextTickAsync(cancellationToken).AsTask();
                continue;
            }

            if (!readTask.Result)
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
                // Confirms the connection immediately, same reasoning and wire shape as the
                // net10.0 writer (an "id:"-less event, not a bare comment, so both TFMs are
                // observable the same way).
                await response.WriteAsync("event: ping\ndata: {}\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);

                // PeriodicTimer.WaitForNextTickAsync does not support a second overlapping call
                // while a previous one is still pending, so the pending call is only ever
                // replaced once it has actually completed, never recreated every loop iteration.
                var heartbeatTask = heartbeat.WaitForNextTickAsync(cancellationToken).AsTask();

                while (true)
                {
                    var readTask = reader.WaitToReadAsync(cancellationToken).AsTask();
                    var completed = await Task.WhenAny(readTask, heartbeatTask);

                    if (completed == heartbeatTask)
                    {
                        if (!heartbeatTask.Result)
                        {
                            return;
                        }

                        await response.WriteAsync(": ping\n\n", cancellationToken);
                        await response.Body.FlushAsync(cancellationToken);
                        heartbeatTask = heartbeat.WaitForNextTickAsync(cancellationToken).AsTask();
                        continue;
                    }

                    if (!readTask.Result)
                    {
                        return;
                    }

                    while (reader.TryRead(out var flowEvent))
                    {
                        await WriteEventAsync(response, flowEvent, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected; nothing left to write.
            }
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
