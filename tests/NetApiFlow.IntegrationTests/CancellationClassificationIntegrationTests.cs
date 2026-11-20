using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

/// <summary>
/// Confirms, against a real Kestrel listener rather than <c>TestServer</c> (same reason as
/// <see cref="DashboardSseTests"/>: only a real client disconnect cancels
/// <c>HttpContext.RequestAborted</c>), that the full pipeline classifies a client disconnecting
/// and an internal cancellation into the right <see cref="FlowStatus"/>, end to end through
/// <c>NetApiFlowMiddleware</c> and <see cref="FlowRecorder"/>.
/// </summary>
public class CancellationClassificationIntegrationTests
{
    private static async Task<(IHost Host, HttpClient Client, CompletedTraces Completed)> CreateHostAsync()
    {
        var completed = new CompletedTraces();

        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseKestrel();
                webBuilder.UseUrls("http://127.0.0.1:0");
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow(o => o.IncludePaths("/**"));
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/client-cancel", async (HttpContext context) =>
                        {
                            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                            return Results.Ok();
                        });

                        endpoints.MapGet("/internal-timeout", async () =>
                        {
                            using var internalTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                            await Task.Delay(TimeSpan.FromSeconds(30), internalTimeout.Token);
                            return Results.Ok();
                        });
                    });
                });
            })
            .Build();

        await host.StartAsync();

        host.Services.GetRequiredService<FlowRecorder>().TraceCompleted += completed.Record;

        var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        var client = new HttpClient { BaseAddress = new Uri(addresses.Single()) };
        return (host, client, completed);
    }

    /// <summary>
    /// Resolves the moment <c>FlowRecorder.TraceCompleted</c> fires for a route, so the wait ends
    /// on the event itself. A polling loop against a short budget reports failure for CPU
    /// contention, which says nothing about whether the trace was classified correctly.
    /// </summary>
    private sealed class CompletedTraces
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<FlowTrace>> _byRouteTemplate = new();

        public void Record(FlowTrace trace) => Awaiter(trace.RouteTemplate).TrySetResult(trace);

        public async Task<FlowTrace> WaitForAsync(string routeTemplate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            return await Awaiter(routeTemplate).Task.WaitAsync(timeout.Token);
        }

        private TaskCompletionSource<FlowTrace> Awaiter(string routeTemplate) =>
            _byRouteTemplate.GetOrAdd(
                routeTemplate,
                _ => new TaskCompletionSource<FlowTrace>(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    [Fact]
    public async Task ClientDisconnectingMidRequest_CompletesTraceAsCancelled()
    {
        var (host, client, completed) = await CreateHostAsync();
        using var _host = host;
        using var _client = client;

        using var clientSideCancellation = new CancellationTokenSource();
        var pendingRequest = client.GetAsync("/client-cancel", clientSideCancellation.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        clientSideCancellation.Cancel();

        await Should.ThrowAsync<Exception>(() => pendingRequest);

        var trace = await completed.WaitForAsync("/client-cancel");
        trace.Status.ShouldBe(FlowStatus.Cancelled);
    }

    [Fact]
    public async Task InternalCancellationWithClientStillConnected_CompletesTraceAsTimeout()
    {
        var (host, client, completed) = await CreateHostAsync();
        using var _host = host;
        using var _client = client;

        using var response = await client.GetAsync("/internal-timeout");

        var trace = await completed.WaitForAsync("/internal-timeout");
        trace.Status.ShouldBe(FlowStatus.Timeout);
    }
}
