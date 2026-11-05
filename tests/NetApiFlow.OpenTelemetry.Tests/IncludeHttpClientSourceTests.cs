using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Shouldly;

namespace NetApiFlow.OpenTelemetry.Tests;

public class IncludeHttpClientSourceTests
{
    private const string SensitiveUrl = "https://api.example.com/accounts?token=super-secret-value";

    private static readonly ActivitySource HttpClientSource = new("System.Net.Http");

    [Fact]
    public void OutboundCall_WithIncludeHttpClientSourceEnabled_ExportsFullUrlWithoutSanitization()
    {
        var exportedItems = new List<Activity>();

        var services = new ServiceCollection();
        services.AddNetApiFlowOtlpExport(options => options.IncludeHttpClientSource = true);
        services.ConfigureOpenTelemetryTracerProvider(builder => builder.AddInMemoryExporter(exportedItems));

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();

        using (var activity = HttpClientSource.StartActivity("HTTP GET"))
        {
            activity?.SetTag("url.full", SensitiveUrl);
        }

        tracerProvider.ForceFlush();

        var exported = exportedItems.ShouldHaveSingleItem();
        exported.GetTagItem("url.full").ShouldBe(SensitiveUrl);
    }

    [Fact]
    public void OutboundCall_WithIncludeHttpClientSourceDisabled_IsNotObserved()
    {
        var exportedItems = new List<Activity>();

        var services = new ServiceCollection();
        services.AddNetApiFlowOtlpExport();
        services.ConfigureOpenTelemetryTracerProvider(builder => builder.AddInMemoryExporter(exportedItems));

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();

        using (var activity = HttpClientSource.StartActivity("HTTP GET"))
        {
            activity?.SetTag("url.full", SensitiveUrl);
        }

        tracerProvider.ForceFlush();

        exportedItems.ShouldBeEmpty();
    }
}
