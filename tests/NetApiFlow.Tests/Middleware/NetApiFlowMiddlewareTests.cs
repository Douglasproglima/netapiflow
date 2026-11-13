using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Middleware;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Middleware;

public class NetApiFlowMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WithParameterizedRoute_RecordsRouteTemplateNotRawPath()
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        FlowTrace? completed = null;
        recorder.TraceCompleted += trace => completed = trace;

        var subject = new NetApiFlowMiddleware(
            next: context =>
            {
                var routePattern = RoutePatternFactory.Parse("/api/orders/{id}");
                var endpoint = new RouteEndpoint(
                    _ => Task.CompletedTask,
                    routePattern,
                    order: 0,
                    metadata: EndpointMetadataCollection.Empty,
                    displayName: "test");
                context.SetEndpoint(endpoint);
                context.Response.StatusCode = 200;
                return Task.CompletedTask;
            },
            recorder,
            options);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/api/orders/42";

        await subject.InvokeAsync(httpContext);

        completed.ShouldNotBeNull();
        completed.RouteTemplate.ShouldBe("/api/orders/{id}");
        completed.HttpStatus.ShouldBe(200);
        completed.Status.ShouldBe(FlowStatus.Success);
        httpContext.Response.Headers["X-NetApiFlow-TraceId"].ToString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_WithoutMatchedEndpoint_RecordsPlaceholderInsteadOfRequestedPath()
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        var recorded = new List<FlowTrace>();
        recorder.TraceStarted += recorded.Add;
        recorder.TraceCompleted += recorded.Add;

        var subject = new NetApiFlowMiddleware(
            next: context =>
            {
                context.Response.StatusCode = 404;
                return Task.CompletedTask;
            },
            recorder,
            options);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/api/devices/359872104738291/status";

        await subject.InvokeAsync(httpContext);

        recorded.ShouldNotBeEmpty();
        recorded.ShouldAllBe(trace => trace.RouteTemplate == "(unmatched)");
    }

    [Fact]
    public async Task InvokeAsync_WhenRouteIsExcluded_DoesNotRecordTrace()
    {
        var netApiFlowOptions = new NetApiFlowOptions();
        netApiFlowOptions.ExcludePaths("/health/**");
        var options = Options.Create(netApiFlowOptions);
        var recorder = new FlowRecorder(options);
        var tracedCount = 0;
        recorder.TraceStarted += _ => tracedCount++;

        var subject = new NetApiFlowMiddleware(next: _ => Task.CompletedTask, recorder, options);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/health/live";

        await subject.InvokeAsync(httpContext);

        tracedCount.ShouldBe(0);
    }

    [Fact]
    public async Task InvokeAsync_WhenHandlerThrows_CompletesTraceAsErrorAndRethrows()
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        FlowTrace? completed = null;
        recorder.TraceCompleted += trace => completed = trace;

        var subject = new NetApiFlowMiddleware(
            next: _ => throw new InvalidOperationException("boom"),
            recorder,
            options);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/orders";

        await Should.ThrowAsync<InvalidOperationException>(() => subject.InvokeAsync(httpContext));

        completed.ShouldNotBeNull();
        completed.Status.ShouldBe(FlowStatus.Error);
        completed.ErrorMessage.ShouldBe("boom");
    }

    [Fact]
    public async Task InvokeAsync_WhenHandlerThrowsOperationCanceledAndRequestAborted_CompletesTraceAsCancelled()
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        FlowTrace? completed = null;
        recorder.TraceCompleted += trace => completed = trace;

        using var requestAborted = new CancellationTokenSource();
        requestAborted.Cancel();

        var subject = new NetApiFlowMiddleware(
            next: _ => throw new OperationCanceledException(requestAborted.Token),
            recorder,
            options);

        var httpContext = new DefaultHttpContext { RequestAborted = requestAborted.Token };
        httpContext.Request.Path = "/api/orders";

        await Should.ThrowAsync<OperationCanceledException>(() => subject.InvokeAsync(httpContext));

        completed.ShouldNotBeNull();
        completed.Status.ShouldBe(FlowStatus.Cancelled);
    }

    [Fact]
    public async Task InvokeAsync_WhenHandlerThrowsOperationCanceledButClientStillConnected_CompletesTraceAsTimeout()
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        FlowTrace? completed = null;
        recorder.TraceCompleted += trace => completed = trace;

        var subject = new NetApiFlowMiddleware(
            next: _ => throw new OperationCanceledException("internal timeout"),
            recorder,
            options);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/orders";

        await Should.ThrowAsync<OperationCanceledException>(() => subject.InvokeAsync(httpContext));

        completed.ShouldNotBeNull();
        completed.Status.ShouldBe(FlowStatus.Timeout);
    }
}
