using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;
using Shouldly;

namespace NetApiFlow.OpenTelemetry.Tests;

public class OwnTracingTests
{
    private const string AppSourceName = "MinhaFonte";
    private static readonly ActivitySource NetApiFlowSource = new("NetApiFlow");
    private static readonly ActivitySource AppSource = new(AppSourceName);
    private static readonly ActivitySource HttpClientSource = new("System.Net.Http");

    [Fact]
    public void AddNetApiFlowOtlpExport_OnAnAppWithItsOwnTracing_ExportsTheSourcesOfTheAppToo()
    {
        var collector = new FakeOtlpCollector();
        var services = new ServiceCollection();
        services.AddOpenTelemetry().WithTracing(builder => builder.AddSource(AppSourceName).AddInMemoryExporter(new List<Activity>()));
        services.AddNetApiFlowOtlpExport(options => options.ConfigureOtlpExporter = collector.Configure);

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();
        using (AppSource.StartActivity("app-work"))
        {
        }

        tracerProvider.ForceFlush();

        collector.ReceivedTheScope(AppSourceName).ShouldBeTrue();
    }

    [Fact]
    public void AddNetApiFlowSource_OnAnAppWithItsOwnTracing_SendsTheStepsToTheExportersOfTheAppAndAddsNothingElse()
    {
        var appExported = new List<Activity>();
        var anyDefaultOtlpExporter = new FakeOtlpCollector();
        var services = new ServiceCollection();
        services.Configure<OtlpExporterOptions>(anyDefaultOtlpExporter.Configure);
        services.AddOpenTelemetry().WithTracing(builder => builder
            .AddSource(AppSourceName)
            .AddNetApiFlowSource()
            .AddInMemoryExporter(appExported));

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();
        using (NetApiFlowSource.StartActivity("netapiflow-step"))
        {
        }

        using (AppSource.StartActivity("app-work"))
        {
        }

        using (HttpClientSource.StartActivity("outbound-call"))
        {
        }

        tracerProvider.ForceFlush();

        appExported.Select(activity => activity.OperationName).Order().ShouldBe(["app-work", "netapiflow-step"]);
        anyDefaultOtlpExporter.Requests.ShouldBeEmpty();
    }
}
