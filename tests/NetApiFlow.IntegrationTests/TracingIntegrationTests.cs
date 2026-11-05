using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class TracingIntegrationTests
{
    private sealed class ThrowingFlowEventListener : IFlowEventListener
    {
        public ValueTask OnEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("listener boom");
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
                    services.AddSingleton<IFlowEventListener, ThrowingFlowEventListener>();
                    services.AddNetApiFlow(o => o.IncludePaths("/**"));
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/orders/{id}", async () =>
                        {
                            using var controllerStep = NetApiFlow.Step("OrdersController", "GetOrder", ComponentType.Controller);
                            using var serviceStep = NetApiFlow.Step("OrderService", "GetAsync", ComponentType.Service);
                            using var repositoryStep = NetApiFlow.Step("OrderRepository", "FindAsync", ComponentType.Repository);
                            await Task.Delay(1);
                            return Results.Ok();
                        });

                        endpoints.MapGet("/api/parallel", async () =>
                        {
                            using var controllerStep = NetApiFlow.Step("ParallelController", "RunParallel", ComponentType.Controller);
                            await Task.WhenAll(DoWorkAsync("A"), DoWorkAsync("B"));
                            return Results.Ok();

                            static async Task DoWorkAsync(string name)
                            {
                                using var step = NetApiFlow.Step("Worker", name, ComponentType.Service);
                                await Task.Delay(10);
                            }
                        });

                        endpoints.MapGet("/api/fail", () =>
                        {
                            using var outerStep = NetApiFlow.Step("FailController", "Fail", ComponentType.Controller);
                            using var innerStep = NetApiFlow.Step("FailService", "DoWork", ComponentType.Service);
                            try
                            {
                                throw new InvalidOperationException("boom");
                            }
                            catch (Exception ex)
                            {
                                innerStep.MarkError(ex);
                            }

                            return Results.StatusCode(500);
                        });

                        endpoints.MapGet("/api/simple", () =>
                        {
                            using var step = NetApiFlow.Step("SimpleController", "Ping", ComponentType.Controller);
                            return Results.Ok();
                        });
                    });
                });
            })
            .Start();
    }

    private static ConcurrentBag<FlowSpan> CaptureSpans(FlowRecorder recorder, string traceId, ConcurrentDictionary<string, ConcurrentBag<FlowSpan>> spansByTrace)
    {
        return spansByTrace.GetOrAdd(traceId, _ => []);
    }

    [Fact]
    public async Task Request_ProducesTraceWithCorrectlyNestedSpans()
    {
        using var host = CreateHost();
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = new ConcurrentDictionary<string, ConcurrentBag<FlowSpan>>();
        void OnSpan(FlowSpan span) => CaptureSpans(recorder, span.TraceId, spansByTrace).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/orders/42");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var spans = spansByTrace[traceId];
        spans.Count.ShouldBe(3);

        var controllerSpan = spans.Single(s => s.Component == "OrdersController");
        var serviceSpan = spans.Single(s => s.Component == "OrderService");
        var repositorySpan = spans.Single(s => s.Component == "OrderRepository");

        serviceSpan.ParentSpanId.ShouldBe(controllerSpan.SpanId);
        repositorySpan.ParentSpanId.ShouldBe(serviceSpan.SpanId);
    }

    [Fact]
    public async Task Request_WithTaskWhenAll_ParallelSpansHaveCorrectParent()
    {
        using var host = CreateHost();
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = new ConcurrentDictionary<string, ConcurrentBag<FlowSpan>>();
        void OnSpan(FlowSpan span) => CaptureSpans(recorder, span.TraceId, spansByTrace).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/parallel");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var spans = spansByTrace[traceId];

        var controllerSpan = spans.Single(s => s.Component == "ParallelController");
        var workerSpans = spans.Where(s => s.Component == "Worker").ToList();

        workerSpans.Count.ShouldBe(2);
        workerSpans.ShouldAllBe(s => s.ParentSpanId == controllerSpan.SpanId);
    }

    [Fact]
    public async Task Request_WithManyConcurrentTaskWhenAllRequests_NeverLosesAWorkerSpan()
    {
        using var host = CreateHost();
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = new ConcurrentDictionary<string, ConcurrentBag<FlowSpan>>();
        void OnSpan(FlowSpan span) => CaptureSpans(recorder, span.TraceId, spansByTrace).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;

        var client = host.GetTestServer().CreateClient();
        var traceIds = await Task.WhenAll(Enumerable.Range(0, 50).Select(async _ =>
        {
            var response = await client.GetAsync("/api/parallel");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        }));

        foreach (var traceId in traceIds)
        {
            var spans = spansByTrace[traceId];
            var controllerSpan = spans.Single(s => s.Component == "ParallelController");
            var workerSpans = spans.Where(s => s.Component == "Worker").ToList();

            workerSpans.Count.ShouldBe(2);
            workerSpans.ShouldAllBe(s => s.ParentSpanId == controllerSpan.SpanId);
        }
    }

    [Fact]
    public async Task Request_WithExceptionInSpan_MarksOriginAndAncestorsAsError()
    {
        using var host = CreateHost();
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = new ConcurrentDictionary<string, ConcurrentBag<FlowSpan>>();
        void OnSpan(FlowSpan span) => CaptureSpans(recorder, span.TraceId, spansByTrace).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/fail");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var spans = spansByTrace[traceId];

        var outerSpan = spans.Single(s => s.Component == "FailController");
        var innerSpan = spans.Single(s => s.Component == "FailService");

        innerSpan.Status.ShouldBe(FlowStatus.Error);
        innerSpan.IsErrorOrigin.ShouldBeTrue();
        outerSpan.Status.ShouldBe(FlowStatus.Error);
        outerSpan.IsErrorOrigin.ShouldBeFalse();
    }

    [Fact]
    public async Task Request_WhenEventListenerThrows_RequestStillSucceeds()
    {
        using var host = CreateHost();
        var client = host.GetTestServer().CreateClient();

        var response = await client.GetAsync("/api/simple");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
