using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    [HttpGet("{id}")]
    public IActionResult Get(int id) => Ok(id);
}

public class MarkingIntegrationTests
{
    private static IHost CreateHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddControllers().AddApplicationPart(typeof(MarkingIntegrationTests).Assembly);
                    services.AddNetApiFlow(o => o.IncludePaths("/**"));
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapControllers();
                        endpoints.MapGet("/api/ping", () => Results.Ok("pong")).WithNetApiFlowTracking();
                    });
                });
            })
            .Start();
    }

    [Fact]
    public async Task ControllerAction_ProducesControllerSpanAutomatically()
    {
        using var host = CreateHost();
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = new ConcurrentDictionary<string, List<FlowSpan>>();
        void OnSpan(FlowSpan span) => spansByTrace.GetOrAdd(span.TraceId, _ => []).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/orders/42");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var span = spansByTrace[traceId].Single(s => s.Type == ComponentType.Controller);
        span.Component.ShouldBe(nameof(OrdersController));
        span.Operation.ShouldBe(nameof(OrdersController.Get));
        span.Status.ShouldBe(FlowStatus.Success);
    }

    [Fact]
    public async Task MinimalApiEndpoint_WithNetApiFlowTracking_ProducesEndpointSpanAutomatically()
    {
        using var host = CreateHost();
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = new ConcurrentDictionary<string, List<FlowSpan>>();
        void OnSpan(FlowSpan span) => spansByTrace.GetOrAdd(span.TraceId, _ => []).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/ping");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var span = spansByTrace[traceId].Single(s => s.Type == ComponentType.Endpoint);
        span.Status.ShouldBe(FlowStatus.Success);
    }
}
