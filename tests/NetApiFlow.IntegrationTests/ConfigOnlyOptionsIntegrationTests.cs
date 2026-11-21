using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

[ApiController]
[Route("api/config-only-ping")]
public sealed class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}

public class ConfigOnlyOptionsIntegrationTests
{
    private sealed class AllowAllFilter : IAsyncAuthorizationFilter
    {
        public Task OnAuthorizationAsync(AuthorizationFilterContext context) => Task.CompletedTask;
    }

    [Fact]
    public async Task LogCorrelation_EnabledOnlyInConfiguration_CorrelatesLogToTheActiveSpan()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Production);
                webBuilder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["NetApiFlow:LogCorrelation:Enabled"] = "true",
                        ["NetApiFlow:LogCorrelation:MinLevel"] = "Warning",
                    }));
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow();
                    services.AddNetApiFlowDashboard();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/warn", (ILoggerFactory loggerFactory) =>
                        {
                            using var step = NetApiFlow.Step("WarnController", "Warn");
                            loggerFactory.CreateLogger("WarnController")
                                .Log(LogLevel.Warning, new EventId(0), "something happened", null, (s, e) => s);
                            return Results.Ok();
                        });
                        endpoints.MapNetApiFlowDashboardApi();
                    });
                });
            })
            .Start();

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spans = new ConcurrentBag<FlowSpan>();
        recorder.SpanCompleted += spans.Add;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/warn");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        spans.Single(span => span.Component == "WarnController").Logs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task PrePipelineSteps_EnabledOnlyInConfiguration_ProducesAuthorizationAndResourceFilterSpans()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Production);
                webBuilder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["NetApiFlow:CapturePrePipelineSteps"] = "true" }));
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddControllers().AddApplicationPart(typeof(PingController).Assembly);
                    services.AddNetApiFlow(options => options.IncludePaths("/api/**"));
                    services.Configure<MvcOptions>(options => options.Filters.Add(new AllowAllFilter()));
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .Start();

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spans = new ConcurrentBag<FlowSpan>();
        recorder.SpanCompleted += spans.Add;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/config-only-ping");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        spans.ShouldContain(span => span.Component == "AuthorizationFilters");
        spans.ShouldContain(span => span.Component == "ResourceFilters");
    }
}
