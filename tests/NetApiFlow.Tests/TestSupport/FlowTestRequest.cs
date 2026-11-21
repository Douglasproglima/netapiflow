using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Instrumentation;
using NetApiFlow.Middleware;
using NetApiFlow.Recording;

namespace NetApiFlow.Tests.TestSupport;

internal static class FlowTestRequest
{
    public static string Begin(FlowRecorder recorder, Activity serverActivity, string method = "GET", string routeTemplate = "/api/test")
    {
        var options = new NetApiFlowOptions();
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        recorder.TryStartTrace(traceId, method, routeTemplate, DateTime.UtcNow);
        var decider = new RequestTracingDecider(Options.Create(options), NullLogger<RequestTracingDecider>.Instance);
        var state = new FlowRequestState(
            recorder, decider, options, new DefaultHttpContext().Response, traceId, serverActivity.TraceId, DateTime.UtcNow, reserved: true, traceByFullPath: true);
        FlowTraceResolver.Attach(serverActivity, state);
        return traceId;
    }
}
