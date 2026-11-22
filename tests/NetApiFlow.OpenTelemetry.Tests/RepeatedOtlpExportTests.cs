using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Shouldly;

namespace NetApiFlow.OpenTelemetry.Tests;

public class RepeatedOtlpExportTests
{
    private static readonly ActivitySource NetApiFlowSource = new("NetApiFlow");

    [Fact]
    public void AddNetApiFlowOtlpExport_CalledTwice_ExportsEachSpanOnce()
    {
        var collector = new FakeOtlpCollector();
        var services = new ServiceCollection();
        services.AddNetApiFlowOtlpExport(options => options.ConfigureOtlpExporter = collector.Configure);
        services.AddNetApiFlowOtlpExport(options => options.ConfigureOtlpExporter = collector.Configure);

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();
        using (NetApiFlowSource.StartActivity("exported-once"))
        {
        }

        tracerProvider.ForceFlush();

        collector.Requests.Count.ShouldBe(1);
    }
}
