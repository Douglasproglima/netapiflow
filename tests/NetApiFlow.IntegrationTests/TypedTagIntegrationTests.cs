using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public enum ShippingStatus
{
    Pending,
    Shipped,
}

public class TypedTagIntegrationTests
{
    private const string TraceIdHeader = "X-NetApiFlow-TraceId";

    [Fact]
    public async Task Tag_WithANumberAFlagOrAnEnum_RecordsItAsInvariantText()
    {
        using var host = await StartHostAsync();

        var response = await host.GetTestServer().CreateClient().GetAsync("/typed-tags");

        var trace = Flow.GetRecentTrace(host.Services, response.Headers.GetValues(TraceIdHeader).Single()).ShouldNotBeNull();
        var tags = trace.Spans.Single(span => span.Type == ComponentType.Service).Tags;
        tags["items"].ShouldBe("3");
        tags["bytes"].ShouldBe("9000000000");
        tags["ratio"].ShouldBe("0.5");
        tags["paid"].ShouldBe("true");
        tags["status"].ShouldBe("Shipped");
        tags["unknown-status"].ShouldBe("42");
    }

    private static async Task<IHost> StartHostAsync()
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGet("/typed-tags", () =>
                    {
                        using var step = Flow.Step("OrderService", "ShipAsync", ComponentType.Service);
                        step.Tag("items", 3);
                        step.Tag("bytes", 9_000_000_000L);
                        step.Tag("ratio", 0.5);
                        step.Tag("paid", true);
                        step.Tag("status", ShippingStatus.Shipped);
                        step.Tag("unknown-status", (ShippingStatus)42);
                        return "shipped";
                    }));
                });
            })
            .StartAsync();
    }
}
