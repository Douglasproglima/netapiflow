using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Instrumentation;
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
    }

    [Fact]
    public async Task InvokeAsync_WithEmitTraceIdHeaderOn_WritesTheHeaderOnlyWhenTheResponseStarts()
    {
        var options = Options.Create(new NetApiFlowOptions { EmitTraceIdHeader = true });
        var recorder = new FlowRecorder(options);
        var subject = new NetApiFlowMiddleware(_ => Task.CompletedTask, recorder, options);
        var responseFeature = new StartingResponseFeature();
        var httpContext = TracedGetRequest(responseFeature);

        await subject.InvokeAsync(httpContext);

        httpContext.Response.Headers.ContainsKey("X-NetApiFlow-TraceId").ShouldBeFalse();
        await responseFeature.RunOnStartingCallbacksAsync();
        var traceId = httpContext.Response.Headers["X-NetApiFlow-TraceId"].ToString();
        traceId.ShouldMatch("^[0-9a-f]{32}$");
        traceId.ShouldNotBe(httpContext.TraceIdentifier);
        recorder.GetRecentlyCompleted(traceId).ShouldNotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_WhenStartingTheTraceThrows_RunsTheRequestUntracedAndReleasesItsReservation()
    {
        var options = Options.Create(new NetApiFlowOptions { EmitTraceIdHeader = true });
        var recorder = new FlowRecorder(options);
        var nextRan = false;
        var seenByTheRequest = true;
        var subject = new NetApiFlowMiddleware(
            _ =>
            {
                nextRan = true;
                seenByTheRequest = FlowTraceResolver.ForCurrentActivity() is not null;
                return Task.CompletedTask;
            },
            recorder,
            options);
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpResponseFeature>(new AlreadyStartedResponseFeature());
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/api/orders/42";

        await subject.InvokeAsync(httpContext);

        nextRan.ShouldBeTrue();
        seenByTheRequest.ShouldBeFalse();
        httpContext.Items.ContainsKey(FlowHttpContextItems.RequestState).ShouldBeFalse();
        recorder.ActiveTraceCount.ShouldBe(0);
    }

    [Fact]
    public async Task InvokeAsync_WhenCompletingTheTraceThrows_StillMarksTheRequestAsFinished()
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        recorder.TraceCompleted += _ => throw new InvalidOperationException("a subscriber failed");
        var subject = new NetApiFlowMiddleware(_ => Task.CompletedTask, recorder, options);
        var httpContext = TracedGetRequest(new StartingResponseFeature());

        await subject.InvokeAsync(httpContext);

        var state = (FlowRequestState)httpContext.Items[FlowHttpContextItems.RequestState]!;
        state.IsRecording.ShouldBeFalse();
    }

    [Fact]
    public async Task InvokeAsync_WhenCompletingTheTraceThrows_ReleasesTheRequestFromAContextCapturedDuringIt()
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        recorder.TraceCompleted += _ => throw new InvalidOperationException("a subscriber failed");
        ExecutionContext? captured = null;
        var subject = new NetApiFlowMiddleware(
            _ =>
            {
                captured = ExecutionContext.Capture();
                return Task.CompletedTask;
            },
            recorder,
            options);
        var httpContext = TracedGetRequest(new StartingResponseFeature());

        await subject.InvokeAsync(httpContext);

        var requestSeen = true;
        ExecutionContext.Run(captured!, _ => requestSeen = FlowTraceResolver.ForCurrentActivity() is not null, null);
        requestSeen.ShouldBeFalse();
    }

    [Fact]
    public async Task InvokeAsync_WithEmitTraceIdHeaderOff_RegistersNothingOnTheResponse()
    {
        var options = Options.Create(new NetApiFlowOptions { EmitTraceIdHeader = false });
        var subject = new NetApiFlowMiddleware(_ => Task.CompletedTask, new FlowRecorder(options), options);
        var responseFeature = new StartingResponseFeature();
        var httpContext = TracedGetRequest(responseFeature);

        await subject.InvokeAsync(httpContext);

        responseFeature.RegisteredCallbacks.ShouldBe(0);
        await responseFeature.RunOnStartingCallbacksAsync();
        httpContext.Response.Headers.ContainsKey("X-NetApiFlow-TraceId").ShouldBeFalse();
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

    private static DefaultHttpContext TracedGetRequest(StartingResponseFeature responseFeature)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpResponseFeature>(responseFeature);
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/api/orders/42";
        return httpContext;
    }

    private sealed class AlreadyStartedResponseFeature : HttpResponseFeature
    {
        public override void OnStarting(Func<object, Task> callback, object state) =>
            throw new InvalidOperationException("The response has already started.");
    }

    private sealed class StartingResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _callbacks = [];

        public int RegisteredCallbacks => _callbacks.Count;

        public override void OnStarting(Func<object, Task> callback, object state) => _callbacks.Add((callback, state));

        public async Task RunOnStartingCallbacksAsync()
        {
            foreach (var (callback, state) in _callbacks)
            {
                await callback(state);
            }
        }
    }
}
