using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
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

        string? errorMessage = null;
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
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

                var routeTemplate = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
                if (routeTemplate is not null)
                {
                    recorder.UpdateRouteTemplate(traceId, routeTemplate);
                }

                var status = errorMessage is not null || context.Response.StatusCode >= 500
                    ? FlowStatus.Error
                    : FlowStatus.Success;

                recorder.CompleteTrace(traceId, context.Response.StatusCode, status, errorMessage);
            }
        }
    }
}
