using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public sealed class MinimalApiHandlers(string answer)
{
    public static IResult StaticGet() => Results.Ok("static");

    public IResult InstanceGet() => Results.Ok(answer);
}

public class MinimalApiEndpointStepNameTests
{
    private const string UnnamedHandlerComponent = "Minimal API";

    [Theory]
    [InlineData("GET", "/lambda", UnnamedHandlerComponent, "GET /lambda")]
    [InlineData("GET", "/named", UnnamedHandlerComponent, "GetNamed")]
    [InlineData("GET", "/static-method", nameof(MinimalApiHandlers), nameof(MinimalApiHandlers.StaticGet))]
    [InlineData("GET", "/instance-method", nameof(MinimalApiHandlers), nameof(MinimalApiHandlers.InstanceGet))]
    [InlineData("GET", "/local-function", UnnamedHandlerComponent, "GET /local-function")]
    [InlineData("GET", "/orders/5", UnnamedHandlerComponent, "GET /orders/{id:int}")]
    [InlineData("GET", "/api/v1/items", UnnamedHandlerComponent, "GET /api/v1/items")]
    [InlineData("PUT", "/multi", UnnamedHandlerComponent, "GET, PUT /multi")]
    public async Task Step_ForAMinimalApiEndpoint_IsNamedFromTheHandlerNotFromTheFrameworkDisplayName(
        string httpMethod,
        string path,
        string expectedComponent,
        string expectedOperation)
    {
        using var host = CreateHost();

        var (trace, endpointStep) = await RunAsync(host, httpMethod, path);

        endpointStep.Component.ShouldBe(expectedComponent);
        endpointStep.Operation.ShouldBe(expectedOperation);
        endpointStep.Component.ShouldNotContain("HTTP:");
        endpointStep.Operation.ShouldNotContain("HTTP:");
        endpointStep.Operation.ShouldNotContain("=>");
        trace.RouteTemplate.ShouldNotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("GET", "/lambda")]
    [InlineData("GET", "/orders/5")]
    [InlineData("GET", "/api/v1/items")]
    [InlineData("PUT", "/multi")]
    public async Task Step_ForAnUnnamedHandler_EndsWithTheRouteTemplateTheTraceRecords(string httpMethod, string path)
    {
        using var host = CreateHost();

        var (trace, endpointStep) = await RunAsync(host, httpMethod, path);

        endpointStep.Operation.ShouldEndWith(trace.RouteTemplate);
    }

    private static async Task<(FlowTrace Trace, FlowSpan EndpointStep)> RunAsync(IHost host, string httpMethod, string path)
    {
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var finished = new ConcurrentDictionary<string, (FlowTrace Trace, IReadOnlyList<FlowSpan> Spans)>();
        recorder.TraceReadyForPersistence += (trace, spans) => finished[trace.TraceId] = (trace, spans);

        using var request = new HttpRequestMessage(new HttpMethod(httpMethod), path);
        using var response = await host.GetTestServer().CreateClient().SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        await WaitUntilAsync(() => finished.ContainsKey(traceId));

        var (finishedTrace, finishedSpans) = finished[traceId];
        return (finishedTrace, finishedSpans.Single(span => span.Type == ComponentType.Endpoint));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        condition().ShouldBeTrue();
    }

    private static IHost CreateHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow(options =>
                    {
                        options.IncludePaths("/**");
                        options.EmitTraceIdHeader = true;
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(MapEveryShapeOfHandler);
                });
            })
            .Start();
    }

    private static void MapEveryShapeOfHandler(IEndpointRouteBuilder endpoints)
    {
        static IResult LocalFunction() => Results.Ok("local");

        endpoints.MapGet("/lambda", () => Results.Ok("lambda")).WithNetApiFlowTracking();
        endpoints.MapGet("/named", () => Results.Ok("named")).WithName("GetNamed").WithNetApiFlowTracking();
        endpoints.MapGet("/static-method", MinimalApiHandlers.StaticGet).WithNetApiFlowTracking();
        endpoints.MapGet("/instance-method", new MinimalApiHandlers("instance").InstanceGet).WithNetApiFlowTracking();
        endpoints.MapGet("/local-function", LocalFunction).WithNetApiFlowTracking();
        endpoints.MapGet("/orders/{id:int}", (int id) => Results.Ok(id)).WithNetApiFlowTracking();
        endpoints.MapMethods("/multi", ["GET", "PUT"], () => Results.Ok("multi")).WithNetApiFlowTracking();

        var group = endpoints.MapGroup("/api/v1").WithNetApiFlowTracking();
        group.MapGet("/items", () => Results.Ok("items"));
    }
}
