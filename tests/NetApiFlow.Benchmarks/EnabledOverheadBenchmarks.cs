namespace NetApiFlow.Benchmarks;

/// <summary>Installed and tracing every request, with default options.</summary>
public class EnabledOverheadBenchmarks : OverheadBenchmarksBase
{
    protected override Action<NetApiFlowOptions>? ConfigureNetApiFlow => _ => { };
}
