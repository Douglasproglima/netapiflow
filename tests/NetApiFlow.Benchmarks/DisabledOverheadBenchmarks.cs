namespace NetApiFlow.Benchmarks;

/// <summary>Installed but turned off (<c>Enabled = false</c>): the middleware and listener are wired up, but every span is dropped by sampling.</summary>
public class DisabledOverheadBenchmarks : OverheadBenchmarksBase
{
    protected override Action<NetApiFlowOptions>? ConfigureNetApiFlow => options => options.Enabled = false;
}
