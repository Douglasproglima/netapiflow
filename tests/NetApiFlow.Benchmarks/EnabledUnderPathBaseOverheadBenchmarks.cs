namespace NetApiFlow.Benchmarks;

public class EnabledUnderPathBaseOverheadBenchmarks : ExcludedRouteOverheadBenchmarksBase
{
    protected override Action<NetApiFlowOptions>? ConfigureNetApiFlow => options => options.ExcludePaths("/excluded");

    protected override string PathBase => "/api";
}
