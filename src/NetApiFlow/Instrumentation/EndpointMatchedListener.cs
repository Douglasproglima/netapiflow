using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetApiFlow.Middleware;
using NetApiFlow.Routing;

namespace NetApiFlow.Instrumentation;

internal sealed partial class EndpointMatchedListener
    : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private const string ListenerName = "Microsoft.AspNetCore";
    private const string EndpointMatchedEventName = "Microsoft.AspNetCore.Routing.EndpointMatched";

    private readonly ILogger<EndpointMatchedListener> _logger;
    private IDisposable? _allListenersSubscription;
    private IDisposable? _listenerSubscription;
    private volatile bool _disposed;

    public EndpointMatchedListener(ILogger<EndpointMatchedListener> logger)
    {
        _logger = logger;
    }

    internal bool IsSubscribed => _allListenersSubscription is not null;

    public void Start()
    {
        if (_disposed || _allListenersSubscription is not null)
        {
            return;
        }

        _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
    }

    public void Dispose()
    {
        _disposed = true;
        _listenerSubscription?.Dispose();
        _allListenersSubscription?.Dispose();
    }

    public void OnNext(DiagnosticListener value)
    {
        if (value.Name == ListenerName)
        {
            _listenerSubscription = value.Subscribe(this, eventName => eventName == EndpointMatchedEventName);
        }
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (_disposed || value.Key != EndpointMatchedEventName || value.Value is not HttpContext context)
        {
            return;
        }

        try
        {
            if (!ReferenceEquals(context.RequestServices?.GetService<EndpointMatchedListener>(), this))
            {
                return;
            }

            var state = context.Items.TryGetValue(FlowHttpContextItems.RequestState, out var item) ? item as FlowRequestState : null;
            state?.OpenForTheMatchedEndpoint(context);
            OpenEndpointStep(context, state?.CaptureMinimalApiEndpoints ?? true);
        }
        catch (Exception ex)
        {
            Log.EventProcessingFailed(_logger, ex);
        }
    }

    public void OnError(Exception error)
    {
    }

    public void OnCompleted()
    {
    }

    private static void OpenEndpointStep(HttpContext context, bool captureMinimalApiEndpoints)
    {
        if (!captureMinimalApiEndpoints
            || !context.Items.ContainsKey(FlowHttpContextItems.TraceStarted)
            || context.Items.ContainsKey(FlowHttpContextItems.ImplicitEndpointStep)
            || context.GetEndpoint() is not RouteEndpoint endpoint
            || endpoint.Metadata.GetMetadata<ActionDescriptor>() is not null
            || endpoint.Metadata.GetMetadata<MethodInfo>() is null)
        {
            return;
        }

        var name = EndpointStepName.For(endpoint);
        var step = NetApiFlow.Step(name.Component, name.Operation, ComponentType.Endpoint);
        if (!step.IsNoOp)
        {
            context.Items[FlowHttpContextItems.ImplicitEndpointStep] = step;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "NetApiFlow: the endpoint step of a Minimal API request could not be opened.")]
        public static partial void EventProcessingFailed(ILogger logger, Exception exception);
    }
}
