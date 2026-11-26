namespace NetApiFlow.Benchmarks;

public class StepDisabledBenchmarks : StepOverheadBenchmarksBase
{
    protected override Action<NetApiFlowOptions> ConfigureNetApiFlow => options => options.Enabled = false;
}
