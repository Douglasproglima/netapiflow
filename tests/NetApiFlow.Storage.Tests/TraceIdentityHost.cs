using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetApiFlow.Domain;
using Flow = NetApiFlow.NetApiFlow;

namespace NetApiFlow.Storage.Tests;

internal sealed class TraceIdentityHost : IAsyncDisposable
{
    internal const string TraceIdHeader = "X-NetApiFlow-TraceId";
    private static readonly TimeSpan StoreDeadline = TimeSpan.FromSeconds(15);

    private readonly WebApplication _app;

    private TraceIdentityHost(WebApplication app, HttpClient client, CapturingLoggerProvider logs)
    {
        _app = app;
        Client = client;
        Logs = logs;
        Store = app.Services.GetRequiredService<ITraceStore>();
    }

    public HttpClient Client { get; }

    public CapturingLoggerProvider Logs { get; }

    public ITraceStore Store { get; }

    public static async Task<TraceIdentityHost> StartAsync(Action<IServiceCollection> addStorage)
    {
        var logs = new CapturingLoggerProvider();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddNetApiFlow(options => options.EmitTraceIdHeader = true);
        addStorage(builder.Services);

        var app = builder.Build();
        app.MapGet("/orders", () => "orders");
        app.MapGet("/stock", () => "stock");
        app.MapGet("/slow", async () =>
        {
            using (Flow.Step("Work", "Slow"))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1500));
            }

            return "slow";
        });
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new TraceIdentityHost(app, new HttpClient { BaseAddress = new Uri(address) }, logs);
    }

    public static string NewTraceparent(string? traceId = null) =>
        $"00-{traceId ?? Guid.NewGuid().ToString("N")}-{Guid.NewGuid().ToString("N")[..16]}-01";

    public async Task<HttpResponseMessage> GetAsync(string path, string? traceparent = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (traceparent is not null)
        {
            request.Headers.Add("traceparent", traceparent);
        }

        return await Client.SendAsync(request);
    }

    public async Task<IReadOnlyList<FlowTraceSummary>> StoredTracesAsync(int expectedCount)
    {
        var deadline = DateTime.UtcNow + StoreDeadline;
        IReadOnlyList<FlowTraceSummary> traces = [];
        while (DateTime.UtcNow < deadline)
        {
            traces = (await Store.QueryAsync(new TraceQuery { PageSize = 50 }, CancellationToken.None)).Items;
            if (traces.Count >= expectedCount)
            {
                break;
            }

            await Task.Delay(50);
        }

        return traces;
    }

    public async Task<IReadOnlyList<FlowSpan>> SpansOfAsync(string traceId)
    {
        var detail = await Store.GetAsync(traceId, CancellationToken.None);
        return detail?.Spans ?? [];
    }

    public IReadOnlyList<CapturingLoggerProvider.LogLine> NetApiFlowWarningsAndErrors() =>
        [.. Logs.Lines.Where(line => line.Level >= LogLevel.Warning && line.Category.StartsWith("NetApiFlow", StringComparison.Ordinal))];

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
