namespace NetApiFlow.Benchmarks;

public class EnabledWithAnExcludedRouteOverheadBenchmarks : ExcludedRouteOverheadBenchmarksBase
{
    protected override Action<NetApiFlowOptions>? ConfigureNetApiFlow => options => options.ExcludePaths("/excluded");
}
