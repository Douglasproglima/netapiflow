namespace NetApiFlow.Benchmarks;

/// <summary>Baseline: <c>AddNetApiFlow</c> is never called, as if the package were not installed.</summary>
public class AbsentOverheadBenchmarks : OverheadBenchmarksBase
{
    protected override Action<NetApiFlowOptions>? ConfigureNetApiFlow => null;
}
