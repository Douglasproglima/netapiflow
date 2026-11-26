using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.TestHost;

namespace NetApiFlow.Benchmarks;

[MemoryDiagnoser]
public abstract class OverheadBenchmarksBase
{
    private const int ConcurrentRequests = 16;

    private TestServer _server = null!;

    protected abstract Action<NetApiFlowOptions>? ConfigureNetApiFlow { get; }

    protected virtual string PathBase => string.Empty;

    [GlobalSetup]
    public void Setup() => _server = BenchmarkHost.Start(ConfigureNetApiFlow, PathBase);

    protected Task Send(string path) => _server.SendAsync(context =>
    {
        context.Request.Method = "GET";
        context.Request.Path = PathBase + path;
    });

    [Benchmark]
    public Task Simple() => Send("/simple");

    [Benchmark]
    public Task Nested() => Send("/nested");

    [Benchmark(OperationsPerInvoke = ConcurrentRequests)]
    public Task NestedConcurrent() => Task.WhenAll(Enumerable.Range(0, ConcurrentRequests).Select(_ => Send("/nested")));
}
