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
[MemoryDiagnoser]
public abstract class OverheadBenchmarksBase
{
    private TestServer _server = null!;

    /// <summary><see langword="null"/> for the "absent" scenario: <c>AddNetApiFlow</c> is never called.</summary>
    protected abstract Action<NetApiFlowOptions>? ConfigureNetApiFlow { get; }

    protected virtual string PathBase => string.Empty;

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
                    if (PathBase.Length > 0)
                    {
                        app.UsePathBase(PathBase);
                    }

                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/simple", () => Results.Ok());
                        endpoints.MapGet("/excluded", () => Results.Ok());
                        endpoints.MapGet("/nested", async () =>
                        {
                            using var controller = Flow.Step("OrdersController", "Get", ComponentType.Controller);
                            using var service = Flow.Step("OrderService", "Process", ComponentType.Service);
                            using var repository = Flow.Step("OrderRepository", "FindAsync", ComponentType.Repository);
                            using var database = Flow.Step("Database", "SELECT orders", ComponentType.Database);
                            await Task.Yield();
                            return Results.Ok();
                        });
                    });
                });
            })
            .Start();

        _server = host.GetTestServer();
    }

    protected Task Send(string path) => _server.SendAsync(context =>
    {
        context.Request.Method = "GET";
        context.Request.Path = PathBase + path;
    });

    /// <summary>One endpoint, no manually marked step: isolates the middleware's own per-request cost.</summary>
    [Benchmark]
    public Task Simple() => Send("/simple");

    /// <summary>Four nested <see cref="Flow.Step"/> calls, as a Controller→Service→Repository→Database request would look.</summary>
    [Benchmark]
    public Task Nested() => Send("/nested");
}
