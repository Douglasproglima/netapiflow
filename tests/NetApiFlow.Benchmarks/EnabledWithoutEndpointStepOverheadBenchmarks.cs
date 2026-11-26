namespace NetApiFlow.Benchmarks;

public class EnabledWithoutEndpointStepOverheadBenchmarks : OverheadBenchmarksBase
{
    protected override Action<NetApiFlowOptions>? ConfigureNetApiFlow => options => options.CaptureMinimalApiEndpoints = false;
}
