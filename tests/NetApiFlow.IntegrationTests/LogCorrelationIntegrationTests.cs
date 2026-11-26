using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

/// <summary>
/// Verifies log correlation is opt-in: the <c>FlowLoggerProvider</c> is always registered, but
/// disabled by default it correlates nothing; enabled, a real <c>ILogger</c> call inside a <see cref="FlowStep"/> reaches the
/// active span's <c>Logs</c>.
/// </summary>
public class LogCorrelationIntegrationTests
{
    private static IHost CreateHost(Action<NetApiFlowOptions> configure)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow(configure);
                    services.AddNetApiFlowDashboard();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/warn", (ILoggerFactory loggerFactory) =>
                        {
                            using var step = Flow.Step("WarnController", "Warn");
                            var logger = loggerFactory.CreateLogger("WarnController");
                            logger.Log(LogLevel.Warning, new EventId(0), "something happened", null, (s, e) => s);
                            return Results.Ok();
                        });
                        endpoints.MapNetApiFlowDashboardApi();
                    });
                });
            })
            .Start();
    }

    private static bool HasFlowLoggerProvider(IHost host) =>
        host.Services.GetServices<ILoggerProvider>().Any(provider => provider.GetType().Name == "FlowLoggerProvider");

    [Fact]
    public async Task Request_WithLogCorrelationDisabledByDefault_CorrelatesNoLogEvenWithTheProviderRegistered()
    {
        using var host = CreateHost(_ => { });

        HasFlowLoggerProvider(host).ShouldBeTrue();

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/warn");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();

        var detailResponse = await client.GetAsync($"/flow/api/traces/{traceId}");
        using var doc = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());

        doc.RootElement.GetProperty("spans").EnumerateArray().Single(span => span.GetProperty("component").GetString() == "WarnController").GetProperty("logs").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Request_WithLogCorrelationEnabled_CorrelatesLogToTheActiveSpan()
    {
        using var host = CreateHost(options =>
        {
            options.LogCorrelation.Enabled = true;
            options.LogCorrelation.MinLevel = LogLevel.Warning;
        });

        HasFlowLoggerProvider(host).ShouldBeTrue();

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/warn");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();

        var detailResponse = await client.GetAsync($"/flow/api/traces/{traceId}");
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());

        var span = doc.RootElement.GetProperty("spans").EnumerateArray().Single(candidate => candidate.GetProperty("component").GetString() == "WarnController");
        var logs = span.GetProperty("logs").EnumerateArray().ToList();

        logs.Count.ShouldBe(1);
        logs[0].GetProperty("level").GetString().ShouldBe("Warning");
        logs[0].GetProperty("message").GetString().ShouldBe("something happened");
    }
}
