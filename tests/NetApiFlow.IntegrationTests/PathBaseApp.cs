using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.Domain;
using NetApiFlow.Mcp;
using NetApiFlow.Mcp.Endpoints;
using NetApiFlow.Recording;

namespace NetApiFlow.IntegrationTests;

internal sealed record PathBaseSetup
{
    public string PathBase { get; init; } = string.Empty;

    public string Environment { get; init; } = Environments.Development;

    public Action<NetApiFlowOptions>? Configure { get; init; }

    public Action<IServiceCollection>? Services { get; init; }

    public Action<IApplicationBuilder>? BeforeRouting { get; init; }

    public Action<IEndpointRouteBuilder>? Map { get; init; }

    public bool Dashboard { get; init; }

    public bool Mcp { get; init; }

    public bool Controllers { get; init; }

    public IFileProvider? StaticFiles { get; init; }
}

internal sealed class PathBaseApp : IAsyncDisposable
{
    public const string TraceIdHeader = "X-NetApiFlow-TraceId";
    public const string ControlRoute = "/control";

    private readonly IHost _host;

    private PathBaseApp(IHost host, string pathBase)
    {
        _host = host;
        PathBase = pathBase;
        Client = host.GetTestServer().CreateClient();
        Recorder = host.Services.GetRequiredService<FlowRecorder>();
        Recorder.TraceStarted += StartedTraces.Enqueue;
        Recorder.TraceCompleted += CompletedTraces.Enqueue;
        Recorder.SpanStarted += StartedSpans.Enqueue;
    }

    public string PathBase { get; }

    public HttpClient Client { get; }

    public FlowRecorder Recorder { get; }

    public IServiceProvider Services => _host.Services;

    public ConcurrentQueue<FlowTrace> StartedTraces { get; } = new();

    public ConcurrentQueue<FlowTrace> CompletedTraces { get; } = new();

    public ConcurrentQueue<FlowSpan> StartedSpans { get; } = new();

    public static async Task<PathBaseApp> StartAsync(PathBaseSetup setup)
    {
        var host = await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(setup.Environment);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    if (setup.Controllers)
                    {
                        services.AddControllers().AddApplicationPart(typeof(OrdersController).Assembly);
                    }

                    services.AddNetApiFlow(options =>
                    {
                        options.EmitTraceIdHeader = true;
                        setup.Configure?.Invoke(options);
                    });
                    if (setup.Dashboard)
                    {
                        services.AddNetApiFlowDashboard();
                    }

                    if (setup.Mcp)
                    {
                        services.AddNetApiFlowMcp();
                    }

                    setup.Services?.Invoke(services);
                });
                webBuilder.Configure(app =>
                {
                    if (setup.PathBase.Length > 0)
                    {
                        app.UsePathBase(setup.PathBase);
                    }

                    setup.BeforeRouting?.Invoke(app);
                    if (setup.StaticFiles is not null)
                    {
                        app.UseStaticFiles(new StaticFileOptions { FileProvider = setup.StaticFiles });
                    }

                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet(ControlRoute, () => "control");
                        setup.Map?.Invoke(endpoints);
                        if (setup.Dashboard)
                        {
                            endpoints.MapNetApiFlowDashboard();
                        }

                        if (setup.Mcp)
                        {
                            endpoints.MapNetApiFlowMcp();
                        }
                    });
                });
            })
            .StartAsync();

        return new PathBaseApp(host, setup.PathBase);
    }

    public Task<HttpResponseMessage> GetAsync(string path) => Client.GetAsync(PathBase + path);

    public Task<HttpResponseMessage> GetWithTraceIdAsync(string path, string traceId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, PathBase + path);
        request.Headers.Add("traceparent", $"00-{traceId}-00f067aa0ba902b7-01");
        return Client.SendAsync(request);
    }

    public Task<FlowTraceDetail> StoredAsync(string traceId) =>
        _host.Services.GetRequiredService<ITraceStore>().WaitForPersistedTraceAsync(traceId);

    public Task<FlowTraceDetail> StoredAsync(HttpResponseMessage response) =>
        StoredAsync(response.Headers.GetValues(TraceIdHeader).Single());

    public async Task<IReadOnlyList<string>> RoutesInTheStoreAsync()
    {
        var control = await GetAsync(ControlRoute);
        var traceId = control.Headers.GetValues(TraceIdHeader).Single();
        var store = _host.Services.GetRequiredService<ITraceStore>();
        await store.WaitForPersistedTraceAsync(traceId);
        var page = await store.QueryAsync(new TraceQuery { PageSize = 200 }, CancellationToken.None);
        return page.Items.Select(trace => trace.RouteTemplate).OrderBy(route => route, StringComparer.Ordinal).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}

internal sealed class SingleFileProvider(string name, string content) : IFileProvider
{
    public IFileInfo GetFileInfo(string subpath) =>
        subpath.TrimStart('/').Equals(name, StringComparison.OrdinalIgnoreCase)
            ? new TextFileInfo(name, content)
            : new NotFoundFileInfo(subpath);

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    private sealed class TextFileInfo(string name, string content) : IFileInfo
    {
        private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(content);

        public bool Exists => true;

        public long Length => _bytes.Length;

        public string? PhysicalPath => null;

        public string Name => name;

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public bool IsDirectory => false;

        public Stream CreateReadStream() => new MemoryStream(_bytes);
    }
}
