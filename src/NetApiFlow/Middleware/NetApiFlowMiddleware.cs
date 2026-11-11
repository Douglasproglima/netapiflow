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

        var started = recorder.TryStartTrace(traceId, context.Request.Method, context.Request.Path.Value ?? "/", DateTime.UtcNow);
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

                // A pre-pipeline step (ADR-0019) opened via DiagnosticListener can be left pending
                // when its filter throws: the matching "after" diagnostic event never fires
                // (confirmed empirically, Fase 23 tarefa 23.4). A downstream UseExceptionHandler
                // middleware can also swallow the exception before it ever reaches the catch block
                // above, so IExceptionHandlerFeature is the fallback source for it. Done before
                // CompleteTrace below, which removes the trace from the recorder's active set:
                // closing the step any later would make FlowActivityListener silently drop the span.
                var exception = caughtException ?? context.Features.Get<IExceptionHandlerFeature>()?.Error;
                context.RequestServices?.GetService<FlowAuthorizationFilterListener>()?.CloseOrphanedSteps(traceId, exception);

                var routeTemplate = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
                if (routeTemplate is not null)
                {
                    recorder.UpdateRouteTemplate(traceId, routeTemplate);
                }

                var status = exception is not null || context.Response.StatusCode >= 500
                    ? FlowStatus.Error
                    : FlowStatus.Success;

                recorder.CompleteTrace(traceId, context.Response.StatusCode, status, exception?.Message);
            }
        }
    }
}
