using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetApiFlow.Domain;

namespace NetApiFlow.Benchmarks;

/// <summary>
/// Drives requests through <see cref="TestServer.SendAsync"/>, which runs the real middleware
/// pipeline (routing, NetApiFlow's own middleware, endpoint execution) without the socket and
/// header-serialization overhead of going through an actual <see cref="HttpClient"/> — that extra
/// layer's own jitter otherwise swamps the few-microsecond difference NetApiFlow is expected to
/// add. Every subclass gets its own process (one BenchmarkDotNet run per type), since the
/// per-process <see cref="System.Diagnostics.ActivitySource"/> listener registered by
/// <c>AddNetApiFlow</c> is process-wide: running "absent" in the same process as "enabled" would
/// corrupt its baseline. Out-of-process (BenchmarkDotNet's default toolchain), not in-process:
/// the "enabled" scenario genuinely records traces in memory up to the 500-trace budget and
/// periodically evicts old ones, producing legitimate latency spikes that trip the in-process
/// toolchain's own execution watchdog.
/// </summary>
public abstract class OverheadBenchmarksBase
{
    private TestServer _server = null!;

    /// <summary><see langword="null"/> for the "absent" scenario: <c>AddNetApiFlow</c> is never called.</summary>
    protected abstract Action<NetApiFlowOptions>? ConfigureNetApiFlow { get; }

    [GlobalSetup]
    public void Setup()
    {
        var host = Host.CreateDefaultBuilder()
            // The default console/debug/event-log providers write on every request (request
            // starting/finished, endpoint executing/executed); that I/O dwarfs the few-microsecond
            // difference being measured here, equally across every scenario, and swamps the signal.
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    if (ConfigureNetApiFlow is { } configure)
                    {
                        services.AddNetApiFlow(configure);
                    }
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/simple", () => Results.Ok());
                        endpoints.MapGet("/nested", async () =>
                        {
                            using var controller = NetApiFlow.Step("OrdersController", "Get", ComponentType.Controller);
                            using var service = NetApiFlow.Step("OrderService", "Process", ComponentType.Service);
                            using var repository = NetApiFlow.Step("OrderRepository", "FindAsync", ComponentType.Repository);
                            using var database = NetApiFlow.Step("Database", "SELECT orders", ComponentType.Database);
                            await Task.Yield();
                            return Results.Ok();
                        });
                    });
                });
            })
            .Start();

        _server = host.GetTestServer();
    }

    /// <summary>One endpoint, no manually marked step: isolates the middleware's own per-request cost.</summary>
    [Benchmark]
    public Task Simple() => _server.SendAsync(context =>
    {
        context.Request.Method = "GET";
        context.Request.Path = "/simple";
    });

    /// <summary>Four nested <see cref="NetApiFlow.Step"/> calls, as a Controller→Service→Repository→Database request would look.</summary>
    [Benchmark]
    public Task Nested() => _server.SendAsync(context =>
    {
        context.Request.Method = "GET";
        context.Request.Path = "/nested";
    });
}
