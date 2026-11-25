namespace NetApiFlow.Benchmarks;

public class StepEnabledBenchmarks : StepOverheadBenchmarksBase
{
    protected override Action<NetApiFlowOptions> ConfigureNetApiFlow => _ => { };
}
