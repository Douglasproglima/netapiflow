using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

[ApiController]
[Route("api/disabled-probe")]
public sealed class DisabledProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        using var step = Flow.Step("Inventory", "Check");
        return Ok();
    }
}

public class DisabledTracingWithAnotherListenerTests
{
    private static IHost StartHost(bool enabled) =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Production);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddControllers().AddApplicationPart(typeof(DisabledProbeController).Assembly);
                    services.AddNetApiFlow(options =>
                    {
                        options.Enabled = enabled;
                        options.IncludePaths("/api/**");
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .Start();

    private static ActivityListener ListenToNetApiFlow(List<string> started)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetApiFlow",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity =>
            {
                lock (started)
                {
                    started.Add(activity.DisplayName);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public async Task Request_WhenTracingIsDisabledAndAnotherListenerSamplesTheSource_RecordsNoTrace()
    {
        using var host = StartHost(enabled: false);
        var started = new List<string>();
        using var other = ListenToNetApiFlow(started);

        var response = await host.GetTestServer().CreateClient().GetAsync("/api/disabled-probe");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("X-NetApiFlow-TraceId").ShouldBeFalse();
        host.Services.GetRequiredService<FlowRecorder>().GetActiveTraces().ShouldBeEmpty();
    }

    [Fact]
    public async Task Request_WhenTracingIsDisabledAndAnotherListenerSamplesTheSource_CreatesNoNetApiFlowActivity()
    {
        using var host = StartHost(enabled: false);
        var started = new List<string>();
        using var other = ListenToNetApiFlow(started);

        var response = await host.GetTestServer().CreateClient().GetAsync("/api/disabled-probe");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        started.ShouldBeEmpty();
    }

    [Fact]
    public async Task Request_WhenTracingIsEnabledAndAnotherListenerSamplesTheSource_StillSharesTheStepWithIt()
    {
        using var host = StartHost(enabled: true);
        var started = new List<string>();
        using var other = ListenToNetApiFlow(started);

        var response = await host.GetTestServer().CreateClient().GetAsync("/api/disabled-probe");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        started.ShouldContain("Inventory.Check");
    }
}
