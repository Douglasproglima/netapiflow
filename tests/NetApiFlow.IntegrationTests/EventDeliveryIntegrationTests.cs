using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class EventDeliveryIntegrationTests
{
    private static readonly TimeSpan ResponseLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GateLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownLimit = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task Request_WithAListenerThatBlocksWhileHandlingAnEvent_RespondsWithoutWaitingForIt()
    {
        using var gate = new ManualResetEventSlim(false);
        var listener = new GatedListener(gate);
        using var host = await StartHostAsync(listener);

        bool respondedWhileTheListenerWasBlocked;
        HttpResponseMessage response;
        try
        {
            var request = host.GetTestServer().CreateClient().GetAsync("/ping");
            respondedWhileTheListenerWasBlocked = await Task.WhenAny(request, Task.Delay(ResponseLimit)) == request;
            gate.Set();
            response = await request;
        }
        finally
        {
            gate.Set();
        }

        respondedWhileTheListenerWasBlocked.ShouldBeTrue("the response waited for the listener");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_WithAListenerThatBlocks_StillDeliversEveryEventOfTheTraceInOrderOnceItIsReleased()
    {
        using var gate = new ManualResetEventSlim(false);
        var listener = new GatedListener(gate);
        using var host = await StartHostAsync(listener);

        var request = host.GetTestServer().CreateClient().GetAsync("/ping");
        await Task.WhenAny(request, Task.Delay(ResponseLimit));
        gate.Set();
        (await request).StatusCode.ShouldBe(HttpStatusCode.OK);

        var delivered = await listener.WaitForAsync(FlowEventType.TraceCompleted);

        delivered.ShouldBeTrue("TraceCompleted never reached the listener");
        var types = listener.Received.Select(flowEvent => flowEvent.Type).ToList();
        types.First().ShouldBe(FlowEventType.TraceStarted);
        types.IndexOf(FlowEventType.TraceCompleted).ShouldBeGreaterThan(types.IndexOf(FlowEventType.SpanCompleted));
        listener.Received.Select(flowEvent => flowEvent.EventId).ShouldBeInOrder(Shouldly.SortDirection.Ascending);
    }

    [Fact]
    public async Task StopAsync_WithASlowListenerAndEventsStillQueued_StopsWithoutDeliveringThem()
    {
        var listener = new SlowListenerIgnoringTheToken(TimeSpan.FromMilliseconds(200));
        var host = await StartHostAsync(listener);
        try
        {
            var client = host.GetTestServer().CreateClient();
            for (var request = 0; request < 10; request++)
            {
                (await client.GetAsync("/ping")).StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            var stopwatch = Stopwatch.StartNew();
            await host.StopAsync();
            stopwatch.Stop();

            stopwatch.Elapsed.ShouldBeLessThan(ShutdownLimit);
            listener.Delivered.ShouldBeLessThan(10 * 4);
        }
        finally
        {
            host.Dispose();
        }
    }

    private static async Task<IHost> StartHostAsync(IFlowEventListener listener)
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(listener);
                    services.AddNetApiFlow();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGet("/ping", () => "pong"));
                });
            })
            .StartAsync();
    }

    private sealed class SlowListenerIgnoringTheToken(TimeSpan delayPerEvent) : IFlowEventListener
    {
        private int _delivered;

        public int Delivered => Volatile.Read(ref _delivered);

        public async ValueTask OnEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken)
        {
            await Task.Delay(delayPerEvent, CancellationToken.None);
            Interlocked.Increment(ref _delivered);
        }
    }

    private sealed class GatedListener(ManualResetEventSlim gate) : IFlowEventListener
    {
        private readonly ConcurrentQueue<FlowEvent> _received = new();

        public IReadOnlyList<FlowEvent> Received => [.. _received];

        public ValueTask OnEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken)
        {
            gate.Wait(GateLimit, CancellationToken.None);
            _received.Enqueue(flowEvent);
            return ValueTask.CompletedTask;
        }

        public async Task<bool> WaitForAsync(FlowEventType type)
        {
            var deadline = DateTime.UtcNow + ResponseLimit;
            while (DateTime.UtcNow < deadline)
            {
                if (_received.Any(flowEvent => flowEvent.Type == type))
                {
                    return true;
                }

                await Task.Delay(20);
            }

            return false;
        }
    }
}
