using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Shouldly;

namespace NetApiFlow.OpenTelemetry.Tests;

public class AddNetApiFlowOtlpExportTests
{
    private static readonly ActivitySource NetApiFlowSource = new("NetApiFlow");

    [Fact]
    public void AddNetApiFlowOtlpExport_WithoutConfiguration_RegistersTracerProviderObservingNetApiFlowSource()
    {
        var services = new ServiceCollection();
        services.AddNetApiFlowOtlpExport();

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();

        tracerProvider.ShouldNotBeNull();

        using var activity = NetApiFlowSource.StartActivity("test-span");

        activity.ShouldNotBeNull();
        activity.Recorded.ShouldBeTrue();
    }

    [Fact]
    public void AddNetApiFlowOtlpExport_RealActivityFromNetApiFlowSource_IsCapturedByTestExporter()
    {
        var exportedItems = new List<Activity>();

        var services = new ServiceCollection();
        services.AddNetApiFlowOtlpExport();
        services.ConfigureOpenTelemetryTracerProvider(builder => builder.AddInMemoryExporter(exportedItems));

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();

        using (var activity = NetApiFlowSource.StartActivity("real-span"))
        {
            activity.ShouldNotBeNull();
        }

        tracerProvider.ForceFlush();

        exportedItems.ShouldContain(a => a.OperationName == "real-span");
    }
}
