using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.TestHost;

namespace NetApiFlow.Benchmarks;

[MemoryDiagnoser]
public abstract class StepOverheadBenchmarksBase
{
    private TestServer _server = null!;

    protected abstract Action<NetApiFlowOptions> ConfigureNetApiFlow { get; }

    [GlobalSetup]
    public void Setup() => _server = BenchmarkHost.Start(ConfigureNetApiFlow, string.Empty);

    [Benchmark(OperationsPerInvoke = BenchmarkHost.StepsPerRequest)]
    public void StepsOutsideAnyRequest() => BenchmarkHost.RunSequentialSteps();

    [Benchmark(OperationsPerInvoke = BenchmarkHost.StepsPerRequest)]
    public Task StepsInsideARequest() => _server.SendAsync(context =>
    {
        context.Request.Method = "GET";
        context.Request.Path = "/steps";
    });
}
