using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using NetApiFlow.Testing;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

/// <summary>
/// Exercises <c>NetApiFlow.Testing</c> the way a user's integration test would: send a request,
/// then assert on the flow it produced, with no step in between to read the header or null-check
/// the lookup.
/// </summary>
public class GetFlowTraceIntegrationTests
{
    private static IHost CreateHost(bool trackingEnabled = true, bool emitTraceIdHeader = true, bool developerExceptionPage = false)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(developerExceptionPage ? Environments.Development : Environments.Production);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow(o =>
                    {
                        o.EmitTraceIdHeader = emitTraceIdHeader;
                        if (trackingEnabled)
                        {
                            o.IncludePaths("/**");
                        }
                        else
                        {
                            o.ExcludePaths("/**");
                        }
                    });
                });
                webBuilder.Configure(app =>
                {
                    if (developerExceptionPage)
                    {
                        app.UseDeveloperExceptionPage();
                    }

                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/boom", () => Boom());
                        endpoints.MapGet("/api/orders/{id}", async () =>
                        {
                            using var controllerStep = NetApiFlow.Step("OrdersController", "GetOrder", ComponentType.Controller);
                            using (NetApiFlow.Step("OrderRepository", "FindAsync", ComponentType.Database))
                            {
                                await Task.Delay(1);
                            }

                            return Results.Ok();
                        });
                    });
                });
            })
            .Start();
    }

    [Fact]
    public async Task GetFlowTrace_AfterATrackedCall_ReturnsThatRequestsTraceAndChainsAssertions()
    {
        using var host = CreateHost();
        var client = host.GetTestServer().CreateClient();

        var response = await client.GetAsync("/api/orders/42");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var expectedTraceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();

        var trace = response.GetFlowTrace(host.Services);

        trace.Trace.TraceId.ShouldBe(expectedTraceId);
        trace
            .ShouldHaveStatus(FlowStatus.Success)
            .ShouldHaveNoErrors()
            .ShouldCallAtMost(ComponentType.Database, 1)
            .ShouldHaveCalled("OrderRepository", "FindAsync")
            .ShouldNeverCall("PaymentGateway");
    }

    [Fact]
    public async Task GetFlowTrace_AfterAnUnhandledExceptionAnsweredByTheDeveloperExceptionPage_ReturnsTheErrorTrace()
    {
        using var host = CreateHost(developerExceptionPage: true);
        var client = host.GetTestServer().CreateClient();

        var response = await client.GetAsync("/api/boom");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.GetFlowTrace(host.Services).ShouldHaveStatus(FlowStatus.Error);
    }

    [Fact]
    public async Task GetFlowTrace_WhenTheRouteIsNotTracked_ThrowsSayingTheHeaderIsMissing()
    {
        using var host = CreateHost(trackingEnabled: false);
        var client = host.GetTestServer().CreateClient();

        var response = await client.GetAsync("/api/orders/42");

        response.Headers.Contains("X-NetApiFlow-TraceId").ShouldBeFalse();

        var thrown = Should.Throw<FlowAssertionException>(() => response.GetFlowTrace(host.Services));

        thrown.Message.ShouldStartWith("The response carries no X-NetApiFlow-TraceId header");
    }

    [Fact]
    public async Task GetFlowTrace_WhenTheHeaderIsTurnedOff_ThrowsNamingTheOption()
    {
        using var host = CreateHost(emitTraceIdHeader: false);
        var client = host.GetTestServer().CreateClient();

        var response = await client.GetAsync("/api/orders/42");

        response.Headers.Contains("X-NetApiFlow-TraceId").ShouldBeFalse();

        var thrown = Should.Throw<FlowAssertionException>(() => response.GetFlowTrace(host.Services));

        thrown.Message.ShouldStartWith("The response carries no X-NetApiFlow-TraceId header");
        thrown.Message.ShouldContain("NetApiFlowOptions.EmitTraceIdHeader");
    }

    [Fact]
    public async Task GetFlowTrace_WhenTheTraceIsNoLongerBuffered_ThrowsInsteadOfReturningNull()
    {
        using var host = CreateHost();
        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/orders/42");

        using var emptyHost = CreateHost();
        var thrown = Should.Throw<FlowAssertionException>(() => response.GetFlowTrace(emptyHost.Services));

        thrown.Message.ShouldStartWith("No trace found for ");
    }

    private static string Boom() => throw new InvalidOperationException("boom");
}
