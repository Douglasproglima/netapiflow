using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

/// <summary>
/// Confirms, against a real Kestrel listener rather than <c>TestServer</c> (the same reason as
/// <see cref="DashboardSseTests"/>: only a real client disconnect cancels
/// <c>HttpContext.RequestAborted</c>), whether reading
/// <c>context.RequestAborted.IsCancellationRequested</c> at the point
/// <c>NetApiFlowMiddleware</c> catches an exception reliably distinguishes a client disconnecting
/// from an unrelated internal cancellation.
/// </summary>
public class CancellationVisibilityInvestigationTests
{
    private sealed record CaughtCancellation(string ExceptionTypeName, bool RequestAbortedAtCatch);

    private static async Task<(IHost Host, HttpClient Client, ConcurrentDictionary<string, CaughtCancellation> Caught)> CreateHostAsync()
    {
        var caught = new ConcurrentDictionary<string, CaughtCancellation>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseKestrel();
                webBuilder.UseUrls("http://127.0.0.1:0");
                webBuilder.ConfigureServices(services => services.AddRouting());
                webBuilder.Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        try
                        {
                            await next(context);
                        }
                        catch (Exception ex)
                        {
                            caught[context.Request.Path.Value!] =
                                new CaughtCancellation(ex.GetType().Name, context.RequestAborted.IsCancellationRequested);
                            throw;
                        }
                    });
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

        var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        var client = new HttpClient { BaseAddress = new Uri(addresses.Single()) };
        return (host, client, caught);
    }

    private static async Task<CaughtCancellation> WaitForCaptureAsync(ConcurrentDictionary<string, CaughtCancellation> caught, string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!caught.TryGetValue(path, out var value))
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(20, CancellationToken.None);
        }

        return caught[path];
    }

    [Fact]
    public async Task RequestAborted_WhenClientDisconnectsMidRequest_IsTrueAtCatch()
    {
        var (host, client, caught) = await CreateHostAsync();
        using var _host = host;
        using var _client = client;

        using var clientSideCancellation = new CancellationTokenSource();
        var pendingRequest = client.GetAsync("/client-cancel", clientSideCancellation.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        clientSideCancellation.Cancel();

        await Should.ThrowAsync<Exception>(() => pendingRequest);

        var result = await WaitForCaptureAsync(caught, "/client-cancel");
        result.RequestAbortedAtCatch.ShouldBeTrue();
    }

    [Fact]
    public async Task RequestAborted_WhenAnInternalCancellationFiresWithTheClientStillConnected_IsFalseAtCatch()
    {
        var (host, client, caught) = await CreateHostAsync();
        using var _host = host;
        using var _client = client;

        using var response = await client.GetAsync("/internal-timeout");

        var result = await WaitForCaptureAsync(caught, "/internal-timeout");
        result.RequestAbortedAtCatch.ShouldBeFalse();
    }
}
