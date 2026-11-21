using BenchmarkDotNet.Attributes;

namespace NetApiFlow.Benchmarks;

public abstract class ExcludedRouteOverheadBenchmarksBase : OverheadBenchmarksBase
{
    [Benchmark]
    public Task Excluded() => Send("/excluded");
}
