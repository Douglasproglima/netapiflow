using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using NetApiFlow.Routing;

namespace NetApiFlow.Middleware;

/// <summary>Opens and closes a <see cref="FlowTrace"/> for each traced request.</summary>
internal sealed class NetApiFlowMiddleware(RequestDelegate next, FlowRecorder recorder, IOptions<NetApiFlowOptions> options)
{
    private const string TraceIdHeaderName = "X-NetApiFlow-TraceId";

    /// <summary>
    /// Route template recorded while routing has not resolved an endpoint yet, and kept for good
    /// on a request that never matches one. This middleware runs before <c>UseRouting</c>, so the
    /// only route-ish value available at that point is the requested URI, which may carry
    /// identifiers and must never reach a trace.
    /// </summary>
    internal const string UnmatchedRouteTemplate = "(unmatched)";

    public async Task InvokeAsync(HttpContext context)
    {
        var flowOptions = options.Value;
        var policy = new TracingRoutePolicy(flowOptions);

        if (!flowOptions.Enabled || !policy.ShouldTrace(context.Request.Path.Value ?? "/"))
        {
            await next(context);
            return;
        }

        var traceId = context.TraceIdentifier;
        if (System.Diagnostics.Activity.Current is { } activity)
        {
            traceId = activity.TraceId.ToString();
        }

        var started = recorder.TryStartTrace(traceId, context.Request.Method, UnmatchedRouteTemplate, DateTime.UtcNow);
        if (started)
        {
            context.Response.Headers[TraceIdHeaderName] = traceId;

            var tenant = context.RequestServices?.GetService<IFlowTenantResolver>()?.Resolve(context);
            if (!string.IsNullOrEmpty(tenant))
            {
                recorder.SetTraceTag(traceId, "tenant", tenant);
            }
        }

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
            if (started)
            {
                // A downstream exception-handler middleware may clear response headers (including
                // the one set above) before producing its own error response, so it is re-applied
                // here as long as nothing has been written to the response body yet.
                if (!context.Response.HasStarted)
                {
                    context.Response.Headers[TraceIdHeaderName] = traceId;
                }

                // An Authorization-filter step opened via DiagnosticListener can be left pending
                // when its own filter throws: the matching "after" diagnostic event never fires.
                // A downstream UseExceptionHandler middleware can also swallow the exception before
                // it ever reaches the catch block above, so IExceptionHandlerFeature is the fallback
                // source, used only to close that orphaned step, never to decide this trace's own
                // status below (an exception a downstream middleware already translated into a
                // non-5xx response is not this trace's error). Done before CompleteTrace, which
                // removes the trace from the recorder's active set: closing the step any later would
                // make FlowActivityListener silently drop the span.
                var orphanedStepException = caughtException ?? context.Features.Get<IExceptionHandlerFeature>()?.Error;
                context.RequestServices?.GetService<FlowAuthorizationFilterListener>()?.CloseOrphanedSteps(traceId, orphanedStepException);

                var routeTemplate = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
                if (routeTemplate is not null)
                {
                    recorder.UpdateRouteTemplate(traceId, routeTemplate);
                }

                var status = caughtException is not null || context.Response.StatusCode >= 500
                    ? FlowStatus.Error
                    : FlowStatus.Success;

                recorder.CompleteTrace(traceId, context.Response.StatusCode, status, caughtException?.Message);
            }
        }
    }
}
