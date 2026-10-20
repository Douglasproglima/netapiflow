using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.Dashboard.Sse;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

/// <summary>
/// Verifies <c>GET /stream</c> (tarefas 3.4/3.5), the replay buffer with <c>Last-Event-ID</c>
/// (tarefa 3.6) and the client/queue limits of risco R-10 (tarefa 3.7).
///
/// Runs against a real Kestrel listener on an ephemeral port, not <c>TestServer</c>: a long-lived
/// streaming response needs each write to reach the client as it happens, and <c>TestServer</c>'s
/// in-memory pipe does not flush <c>TypedResults.ServerSentEvents</c>' writes until the connection
/// has something else to say (confirmed by comparison against a manual <c>WriteAsync</c>/
/// <c>FlushAsync</c> loop, which DID deliver promptly under <c>TestServer</c>). Real Kestrel does
/// not have this gap, so it is also closer to what a real client actually experiences.
/// </summary>
public class DashboardSseTests
{
    private static async Task<(IHost Host, HttpClient Client)> CreateHostAsync()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseKestrel();
                webBuilder.UseUrls("http://127.0.0.1:0");
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow(o => o.IncludePaths("/api/**"));
                    services.AddNetApiFlowDashboard();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/ping", () => "pong").WithNetApiFlowTracking();
                        endpoints.MapNetApiFlowDashboardApi();
                    });
                });
            })
            .Build();

        await host.StartAsync();

        var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        var client = new HttpClient { BaseAddress = new Uri(addresses.Single()) };
        return (host, client);
    }

    private static async Task<(HttpResponseMessage Response, StreamReader? Reader)> ConnectAsync(HttpClient client, string? lastEventId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/flow/api/stream");
        if (lastEventId is not null)
        {
            request.Headers.Add("Last-Event-ID", lastEventId);
        }

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return (response, null);
        }

        var stream = await response.Content.ReadAsStreamAsync();
        var reader = new StreamReader(stream);

        // The server sends an immediate "ping" the moment the connection opens, to flush response
        // headers right away instead of waiting on the first real event or heartbeat tick.
        var connectEvent = await ReadEventAsync(reader);
        connectEvent.EventType.ShouldBe("ping");

        return (response, reader);
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var lineTask = reader.ReadLineAsync(cts.Token).AsTask();
        var completed = await Task.WhenAny(lineTask, Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None));
        return completed == lineTask ? await lineTask : throw new TimeoutException("Timed out waiting for an SSE line.");
    }

    private static async Task<(long? EventId, string EventType, JsonDocument Data)> ReadEventAsync(StreamReader reader)
    {
        string? id = null;
        string? eventType = null;
        string? data = null;

        string? line;
        while ((line = await ReadLineWithTimeoutAsync(reader)) is not null)
        {
            if (line.Length == 0)
            {
                if (data is not null)
                {
                    break;
                }

                continue;
            }

            if (line.StartsWith("id: ", StringComparison.Ordinal))
            {
                id = line[4..];
            }
            else if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventType = line[7..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data = line[6..];
            }
        }

        return (id is null ? null : long.Parse(id, CultureInfo.InvariantCulture), eventType!, JsonDocument.Parse(data!));
    }

    [Fact]
    public async Task Stream_ForATrackedRequest_DeliversEventsInOrderWithMatchingTraceId()
    {
        var (host, client) = await CreateHostAsync();
        using var _host = host;
        using var _client = client;

        var (response, reader) = await ConnectAsync(client);
        using var _ = response;
        reader.ShouldNotBeNull();

        var pingResponse = await client.GetAsync("/api/ping");
        var traceId = pingResponse.Headers.GetValues("X-NetApiFlow-TraceId").Single();

        var first = await ReadEventAsync(reader);
        var second = await ReadEventAsync(reader);
        var third = await ReadEventAsync(reader);
        var fourth = await ReadEventAsync(reader);

        first.EventType.ShouldBe("TraceStarted");
        second.EventType.ShouldBe("SpanStarted");
        third.EventType.ShouldBe("SpanCompleted");
        fourth.EventType.ShouldBe("TraceCompleted");

        first.Data.RootElement.GetProperty("traceId").GetString().ShouldBe(traceId);
        third.Data.RootElement.GetProperty("span").GetProperty("type").GetString().ShouldBe("Endpoint");

        second.EventId!.Value.ShouldBeGreaterThan(first.EventId!.Value);
        third.EventId!.Value.ShouldBeGreaterThan(second.EventId!.Value);
        fourth.EventId!.Value.ShouldBeGreaterThan(third.EventId!.Value);
    }

    [Fact]
    public async Task Stream_ReconnectWithLastEventId_ReplaysMissedEventsWithoutGapOrDuplicate()
    {
        var (host, client) = await CreateHostAsync();
        using var _host = host;
        using var _client = client;
        var recorder = host.Services.GetRequiredService<FlowRecorder>();

        var (firstResponse, firstReader) = await ConnectAsync(client);
        firstReader.ShouldNotBeNull();

        recorder.TryStartTrace("reconnect-1", "GET", "/api/orders", DateTime.UtcNow);
        var firstEvent = await ReadEventAsync(firstReader);
        firstEvent.EventType.ShouldBe("TraceStarted");

        firstResponse.Dispose(); // simulate the client disconnecting

        // Events born while the client is away: missed by the live broadcast, recoverable only via replay.
        recorder.TryStartTrace("reconnect-2", "GET", "/api/orders", DateTime.UtcNow);
        recorder.CompleteTrace("reconnect-2", 200, FlowStatus.Success, null);

        var (secondResponse, secondReader) = await ConnectAsync(client, lastEventId: firstEvent.EventId!.Value.ToString(CultureInfo.InvariantCulture));
        using var _ = secondResponse;
        secondReader.ShouldNotBeNull();

        var replayed1 = await ReadEventAsync(secondReader);
        var replayed2 = await ReadEventAsync(secondReader);

        replayed1.EventId.ShouldBe(firstEvent.EventId + 1);
        replayed1.Data.RootElement.GetProperty("traceId").GetString().ShouldBe("reconnect-2");
        replayed2.EventId.ShouldBe(firstEvent.EventId + 2);
        replayed2.Data.RootElement.GetProperty("traceId").GetString().ShouldBe("reconnect-2");
    }

    [Fact]
    public async Task Stream_FiftyFirstConcurrentClient_Returns503()
    {
        var (host, client) = await CreateHostAsync();
        using var _host = host;
        using var _client = client;
        var connections = new List<HttpResponseMessage>();

        try
        {
            for (var i = 0; i < SseBroadcaster.MaxClients; i++)
            {
                var (response, reader) = await ConnectAsync(client);
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                reader.ShouldNotBeNull();
                connections.Add(response);
            }

            var (rejected, rejectedReader) = await ConnectAsync(client);
            using var __ = rejected;
            rejected.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            rejectedReader.ShouldBeNull();
        }
        finally
        {
            foreach (var response in connections)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task SlowConsumer_NeverGrowsThePerClientQueueBeyondItsBoundedCapacity()
    {
        var broadcaster = new SseBroadcaster();
        using var subscription = broadcaster.TrySubscribe(lastEventId: null);
        subscription.ShouldNotBeNull();

        for (var i = 0; i < SseBroadcaster.PerClientQueueCapacity * 3; i++)
        {
            await broadcaster.OnEventAsync(new FlowEvent
            {
                EventId = i,
                Type = FlowEventType.SpanStarted,
                TraceId = "flood",
                Timestamp = DateTime.UtcNow,
            }, CancellationToken.None);
        }

        subscription.Reader.Count.ShouldBeLessThanOrEqualTo(SseBroadcaster.PerClientQueueCapacity);
    }
}
