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
    private static async Task<(IHost Host, HttpClient Client, ConcurrentDictionary<string, FlowTrace> Completed)> CreateHostAsync()
    {
        var completed = new ConcurrentDictionary<string, FlowTrace>();

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

        host.Services.GetRequiredService<FlowRecorder>().TraceCompleted += trace => completed[trace.RouteTemplate] = trace;

        var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        var client = new HttpClient { BaseAddress = new Uri(addresses.Single()) };
        return (host, client, completed);
    }

    private static async Task<FlowTrace> WaitForCompletionAsync(ConcurrentDictionary<string, FlowTrace> completed, string routeTemplate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!completed.TryGetValue(routeTemplate, out var trace))
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(20, CancellationToken.None);
        }

        return completed[routeTemplate];
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

        var trace = await WaitForCompletionAsync(completed, "/client-cancel");
        trace.Status.ShouldBe(FlowStatus.Cancelled);
    }

    [Fact]
    public async Task InternalCancellationWithClientStillConnected_CompletesTraceAsTimeout()
    {
        var (host, client, completed) = await CreateHostAsync();
        using var _host = host;
        using var _client = client;

        using var response = await client.GetAsync("/internal-timeout");

        var trace = await WaitForCompletionAsync(completed, "/internal-timeout");
        trace.Status.ShouldBe(FlowStatus.Timeout);
    }
}
