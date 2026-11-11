using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Diagnostics;
using Microsoft.Extensions.Logging;

namespace NetApiFlow.Instrumentation;

/// <summary>
/// Opens a <see cref="ComponentType.Service"/> step per Authorization filter instance, via
/// <see cref="DiagnosticListener.AllListeners"/> (ADR-0019). <see cref="Microsoft.AspNetCore.Mvc.Filters.IAsyncAuthorizationFilter"/>
/// has no <c>next()</c> delegate, so a native filter cannot wrap it the way
/// <see cref="Middleware.FlowResourceFilter"/> wraps the Resource stage.
/// </summary>
internal sealed partial class FlowAuthorizationFilterListener
    : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private const string ListenerName = "Microsoft.AspNetCore";
    private const string BeforeEventName = "Microsoft.AspNetCore.Mvc.BeforeOnAuthorization";
    private const string AfterEventName = "Microsoft.AspNetCore.Mvc.AfterOnAuthorization";

    private readonly ConcurrentDictionary<(string TraceId, object Filter), FlowStep> _pendingSteps = new();
    private readonly ILogger<FlowAuthorizationFilterListener> _logger;
    private readonly IDisposable _allListenersSubscription;
    private IDisposable? _listenerSubscription;

    public FlowAuthorizationFilterListener(ILogger<FlowAuthorizationFilterListener> logger)
    {
        _logger = logger;
        _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
    }

    public void Dispose()
    {
        _listenerSubscription?.Dispose();
        _allListenersSubscription.Dispose();
    }

    /// <summary>
    /// Closes any step still pending for <paramref name="traceId"/>, marked as failed. An exception
    /// thrown inside an Authorization filter's own body never reaches <c>AfterOnAuthorization</c>
    /// (confirmed empirically, Fase 23 tarefa 23.4): the diagnostic event pair is only written
    /// around a normal return, so the step opened in <c>BeforeOnAuthorization</c> would otherwise
    /// never close. Called from <see cref="Middleware.NetApiFlowMiddleware"/>'s
    /// <c>HttpContext.Response.OnCompleted</c> callback, the only hook that still runs when a
    /// downstream <c>UseExceptionHandler</c> middleware already caught the exception. A step found
    /// pending at that point is, by construction, always the result of a failure, even when
    /// <paramref name="exception"/> itself could not be recovered.
    /// </summary>
    internal void CloseOrphanedSteps(string traceId, Exception? exception)
    {
        foreach (var key in _pendingSteps.Keys)
        {
            if (key.TraceId != traceId)
            {
                continue;
            }

            if (_pendingSteps.TryRemove(key, out var step))
            {
                step.MarkError(exception ?? new InvalidOperationException(
                    "NetApiFlow: an Authorization filter never completed, most likely because it threw."));
                step.Dispose();
            }
        }
    }

    public void OnNext(DiagnosticListener value)
    {
        if (value.Name == ListenerName)
        {
            _listenerSubscription = value.Subscribe(this);
        }
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        try
        {
            switch (value.Key)
            {
                case BeforeEventName when value.Value is BeforeAuthorizationFilterOnAuthorizationEventData before:
                    OnBefore(before);
                    break;
                case AfterEventName when value.Value is AfterAuthorizationFilterOnAuthorizationEventData after:
                    OnAfter(after);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.EventProcessingFailed(_logger, value.Key, ex);
        }
    }

    public void OnError(Exception error)
    {
    }

    public void OnCompleted()
    {
    }

    private void OnBefore(BeforeAuthorizationFilterOnAuthorizationEventData data)
    {
        if (NetApiFlow.CurrentTraceId is not { } traceId)
        {
            return;
        }

        var step = NetApiFlow.Step("AuthorizationFilters", data.Filter.GetType().Name, ComponentType.Service);
        _pendingSteps[(traceId, data.Filter)] = step;
    }

    private void OnAfter(AfterAuthorizationFilterOnAuthorizationEventData data)
    {
        if (NetApiFlow.CurrentTraceId is not { } traceId)
        {
            return;
        }

        if (_pendingSteps.TryRemove((traceId, data.Filter), out var step))
        {
            step.Dispose();
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "NetApiFlow: pre-pipeline event processing failed for {DiagnosticEvent}")]
        public static partial void EventProcessingFailed(ILogger logger, string diagnosticEvent, Exception exception);
    }
}
