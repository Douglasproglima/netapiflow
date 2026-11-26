using System.Collections.Concurrent;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Sse;
using NetApiFlow.Domain;
using NetApiFlow.MediatR;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public sealed record PlaceRepeatedOrder : IRequest<string>;

public sealed class PlaceRepeatedOrderHandler : IRequestHandler<PlaceRepeatedOrder, string>
{
    public Task<string> Handle(PlaceRepeatedOrder request, CancellationToken cancellationToken) => Task.FromResult("placed");
}

public class RepeatedRegistrationIntegrationTests
{
    private const string TraceIdHeader = "X-NetApiFlow-TraceId";
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task AddNetApiFlow_CalledTwiceWithDifferentConfigure_AppliesBothInTheOrderOfTheCalls()
    {
        using var host = await StartHostAsync(services =>
        {
            services.AddNetApiFlow(options => options.SlowThreshold = TimeSpan.FromSeconds(1));
            services.AddNetApiFlow(options =>
            {
                options.SlowThreshold = TimeSpan.FromSeconds(2);
                options.ExcludePaths("/ignored");
            });
        });
        var started = new ConcurrentQueue<FlowTrace>();
        host.Services.GetRequiredService<FlowRecorder>().TraceStarted += started.Enqueue;
        var client = host.GetTestServer().CreateClient();

        await client.GetAsync("/ignored");
        await client.GetAsync("/ping");

        var options = host.Services.GetRequiredService<IOptions<NetApiFlowOptions>>().Value;
        options.SlowThreshold.ShouldBe(TimeSpan.FromSeconds(2));
        options.ExcludePatterns.ShouldBe(["/ignored"]);
        started.Select(trace => trace.RouteTemplate).ShouldBe(["/ping"]);
    }

    [Fact]
    public async Task AddNetApiFlowDashboard_CalledTwice_RegistersOneListenerAndDeliversEachEventOnce()
    {
        using var host = await StartHostAsync(services =>
        {
            services.AddNetApiFlow();
            services.AddNetApiFlowDashboard();
            services.AddNetApiFlowDashboard();
        });
        using var subscription = host.Services.GetRequiredService<SseBroadcaster>().TrySubscribe(lastEventId: null).ShouldNotBeNull();

        var response = await host.GetTestServer().CreateClient().GetAsync("/ping");
        var traceId = response.Headers.GetValues(TraceIdHeader).Single();
        var received = await ReadUntilTheTraceCompletesAsync(subscription, traceId);

        host.Services.GetServices<IFlowEventListener>().Count().ShouldBe(1);
        received.Count(flowEvent => flowEvent.Type == FlowEventType.TraceStarted).ShouldBe(1);
        received.Count(flowEvent => flowEvent.Type == FlowEventType.TraceCompleted).ShouldBe(1);
    }

    [Fact]
    public async Task AddNetApiFlowMediatR_CalledTwice_RecordsOneStepPerHandlerCall()
    {
        using var host = await StartHostAsync(
            services =>
            {
                services.AddNetApiFlow();
                services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<PlaceRepeatedOrder>());
                services.AddNetApiFlowMediatR();
                services.AddNetApiFlowMediatR();
            },
            endpoints => endpoints.MapGet("/place", async (ISender sender) => await sender.Send(new PlaceRepeatedOrder())));

        var response = await host.GetTestServer().CreateClient().GetAsync("/place");
        var traceId = response.Headers.GetValues(TraceIdHeader).Single();
        var trace = await host.Services.GetRequiredService<ITraceStore>().WaitForPersistedTraceAsync(traceId);

        trace.Spans.Count(span => span.Type == ComponentType.UseCase).ShouldBe(1);
    }

    private static async Task<List<FlowEvent>> ReadUntilTheTraceCompletesAsync(SseSubscription subscription, string traceId)
    {
        var received = new List<FlowEvent>();
        using var timeout = new CancellationTokenSource(WaitLimit);
        while (!received.Any(flowEvent => flowEvent.TraceId == traceId && flowEvent.Type == FlowEventType.TraceCompleted))
        {
            received.Add(await subscription.Reader.ReadAsync(timeout.Token));
        }

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        while (subscription.Reader.TryRead(out var late))
        {
            received.Add(late);
        }

        return [.. received.Where(flowEvent => flowEvent.TraceId == traceId)];
    }

    private static async Task<IHost> StartHostAsync(Action<IServiceCollection> register, Action<IEndpointRouteBuilder>? map = null)
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    register(services);
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/ping", () => "pong");
                        endpoints.MapGet("/ignored", () => "ignored");
                        map?.Invoke(endpoints);
                    });
                });
            })
            .StartAsync();
    }
}
