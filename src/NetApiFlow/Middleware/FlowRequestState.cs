using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Recording;
using NetApiFlow.Routing;

namespace NetApiFlow.Middleware;

internal sealed class FlowRequestState
{
    private const string TraceIdHeaderName = "X-NetApiFlow-TraceId";

    private readonly FlowRecorder _recorder;
    private readonly RequestTracingDecider _decider;
    private readonly NetApiFlowOptions _options;
    private readonly HttpResponse _response;
    private readonly DateTime _startedAtUtc;
    private readonly bool _traceByFullPath;
    private bool _reservationHeld;
    private bool _decided;
    private bool _headerRegistered;

    public FlowRequestState(
        FlowRecorder recorder,
        RequestTracingDecider decider,
        NetApiFlowOptions options,
        HttpResponse response,
        string traceId,
        DateTime startedAtUtc,
        bool reserved,
        bool traceByFullPath)
    {
        _recorder = recorder;
        _decider = decider;
        _options = options;
        _response = response;
        TraceId = traceId;
        _startedAtUtc = startedAtUtc;
        _reservationHeld = reserved;
        _traceByFullPath = traceByFullPath;
    }

    public string TraceId { get; }

    public bool Opened { get; private set; }

    public bool CaptureMinimalApiEndpoints => _options.CaptureMinimalApiEndpoints;

    public void RegisterHeaderWhenTheFullPathAccepts()
    {
        if (_traceByFullPath && _reservationHeld)
        {
            RegisterHeader();
        }
    }

    public void OpenForTheMatchedEndpoint(HttpContext context)
    {
        if (_decided)
        {
            return;
        }

        _decided = true;
        try
        {
            if (!AcceptsTheMatchedEndpoint(context))
            {
                ReleaseReservation();
                return;
            }

            var routeTemplate = context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { } rawText }
                ? RouteTemplateNormalizer.Normalize(rawText)
                : NetApiFlowMiddleware.UnmatchedRouteTemplate;
            Open(context, routeTemplate);
        }
        catch (Exception exception)
        {
            _decider.LogFailure(exception);
            ReleaseReservation();
        }
    }

    private static bool IsReExecution(HttpContext context) =>
        context.Features.Get<IStatusCodeReExecuteFeature>() is not null
        || context.Features.Get<IExceptionHandlerFeature>() is not null;

    private bool AcceptsTheMatchedEndpoint(HttpContext context) =>
        IsReExecution(context)
            ? _traceByFullPath
            : _decider.ShouldTraceMatchedEndpoint(context.GetEndpoint(), context.Request.Path.Value ?? "/");

    public void OpenWhenNoEndpointWasMatched(HttpContext context)
    {
        if (_decided || !_traceByFullPath)
        {
            return;
        }

        _decided = true;
        try
        {
            Open(context, NetApiFlowMiddleware.UnmatchedRouteTemplate);
        }
        catch (Exception exception)
        {
            _decider.LogFailure(exception);
        }
    }

    public void ReleaseReservation()
    {
        if (_reservationHeld && !Opened)
        {
            _recorder.DiscardReserved(TraceId);
        }

        _reservationHeld = false;
    }

    private void Open(HttpContext context, string routeTemplate)
    {
        var published = _reservationHeld
            ? _recorder.PublishReserved(TraceId, routeTemplate)
            : _recorder.TryStartTrace(TraceId, context.Request.Method, routeTemplate, _startedAtUtc);
        if (!published)
        {
            return;
        }

        Opened = true;
        _reservationHeld = false;
        context.Items[FlowHttpContextItems.TraceStarted] = true;
        if (_options.EmitTraceIdHeader)
        {
            RegisterHeader();
        }

        var tenant = context.RequestServices?.GetService<IFlowTenantResolver>()?.Resolve(context);
        if (!string.IsNullOrEmpty(tenant))
        {
            _recorder.SetTraceTag(TraceId, "tenant", tenant);
        }
    }

    private void RegisterHeader()
    {
        if (_headerRegistered)
        {
            return;
        }

        _headerRegistered = true;
        _response.OnStarting(WriteTraceIdHeaderWhenRecorded, this);
    }

    private static Task WriteTraceIdHeaderWhenRecorded(object state)
    {
        var request = (FlowRequestState)state;
        var recorded = request.Opened || (!request._decided && request._traceByFullPath && request._reservationHeld);
        if (recorded)
        {
            request._response.Headers[TraceIdHeaderName] = request.TraceId;
        }

        return Task.CompletedTask;
    }
}
