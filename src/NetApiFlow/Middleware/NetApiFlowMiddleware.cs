using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using NetApiFlow.Routing;

namespace NetApiFlow.Middleware;

internal sealed class NetApiFlowMiddleware(
    RequestDelegate next,
    FlowRecorder recorder,
    IOptions<NetApiFlowOptions> options,
    RequestTracingDecider decider)
{
    internal const string UnmatchedRouteTemplate = "(unmatched)";

    public NetApiFlowMiddleware(RequestDelegate next, FlowRecorder recorder, IOptions<NetApiFlowOptions> options)
        : this(next, recorder, options, new RequestTracingDecider(options, NullLogger<RequestTracingDecider>.Instance))
    {
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var flowOptions = options.Value;
        if (!flowOptions.Enabled)
        {
            await next(context);
            return;
        }

        var state = StartRequest(context, flowOptions);

        Exception? caughtException = null;
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            caughtException = ex;
            throw;
        }
        finally
        {
            FinishRequest(context, state, caughtException);
            FlowTraceResolver.Detach();
        }
    }

    private FlowRequestState StartRequest(HttpContext context, NetApiFlowOptions flowOptions)
    {
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var serverActivity = Activity.Current;
        var startedAtUtc = DateTime.UtcNow;
        var reserved = recorder.TryReserveTrace(traceId, context.Request.Method, startedAtUtc);
        var traceByFullPath = decider.ShouldTraceFullPath(context.Request.Path.Value ?? "/");
        var state = new FlowRequestState(
            recorder, decider, flowOptions, context.Response, traceId, serverActivity?.TraceId ?? default, startedAtUtc, reserved, traceByFullPath);
        FlowTraceResolver.Attach(serverActivity, state);
        context.Items[FlowHttpContextItems.RequestState] = state;
        if (flowOptions.EmitTraceIdHeader)
        {
            state.RegisterHeaderWhenTheFullPathAccepts();
        }

        return state;
    }

    private void FinishRequest(HttpContext context, FlowRequestState state, Exception? caughtException)
    {
        state.OpenWhenNoEndpointWasMatched(context);
        state.ReleaseReservation();
        if (state.Opened)
        {
            CompleteTrace(context, state.TraceId, caughtException);
        }

        state.MarkFinished();
    }

    private void CompleteTrace(HttpContext context, string traceId, Exception? caughtException)
    {
        var stepFailure = caughtException ?? context.Features.Get<IExceptionHandlerFeature>()?.Error;
        CloseStepsLeftOpen(context, traceId, stepFailure);

        var routeTemplate = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        if (routeTemplate is not null)
        {
            recorder.UpdateRouteTemplate(traceId, RouteTemplateNormalizer.Normalize(routeTemplate));
        }

        var status = caughtException switch
        {
            OperationCanceledException when context.RequestAborted.IsCancellationRequested => FlowStatus.Cancelled,
            OperationCanceledException => FlowStatus.Timeout,
            not null => FlowStatus.Error,
            null when context.Response.StatusCode >= 500 => FlowStatus.Error,
            _ => FlowStatus.Success,
        };

        recorder.CompleteTrace(traceId, context.Response.StatusCode, status, caughtException?.Message);
    }

    private static void CloseStepsLeftOpen(HttpContext context, string traceId, Exception? failure)
    {
        context.RequestServices?.GetService<FlowAuthorizationFilterListener>()?.CloseOrphanedSteps(traceId, failure);
        CloseImplicitEndpointStep(context, failure);
    }

    private static void CloseImplicitEndpointStep(HttpContext context, Exception? failure)
    {
        if (!context.Items.Remove(FlowHttpContextItems.ImplicitEndpointStep, out var opened) || opened is not FlowStep step)
        {
            return;
        }

        failure ??= context.Items.TryGetValue(FlowHttpContextItems.SwallowedException, out var swallowed) ? swallowed as Exception : null;

        if (failure is not null)
        {
            step.MarkError(failure);
        }
        else if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            step.MarkFailedWithoutException();
        }

        step.Dispose();
    }
}
