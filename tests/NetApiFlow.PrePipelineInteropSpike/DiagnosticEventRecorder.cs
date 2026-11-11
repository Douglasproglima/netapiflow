using System.Collections.Concurrent;
using System.Diagnostics;

namespace NetApiFlow.PrePipelineInteropSpike;

internal sealed record RecordedDiagnosticEvent(
    string ListenerName,
    string EventName,
    object? Payload,
    string? ActivityTraceId,
    string? ActivitySpanId,
    string? ActivityOperationName)
{
    public string Describe() =>
        $"{EventName} activityTraceId={ActivityTraceId ?? "<null>"} activityOperationName={ActivityOperationName ?? "<null>"}";
}

internal sealed class DiagnosticEventRecorder : IDisposable
{
    private readonly string _listenerNameContains;
    private readonly ConcurrentQueue<RecordedDiagnosticEvent> _events = new();
    private readonly ConcurrentBag<IDisposable> _listenerSubscriptions = new();
    private readonly IDisposable _allListenersSubscription;

    public DiagnosticEventRecorder(string listenerNameContains)
    {
        _listenerNameContains = listenerNameContains;
        _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(
            new DelegatingObserver<DiagnosticListener>(OnNewListener));
    }

    public IReadOnlyList<RecordedDiagnosticEvent> Events => [.. _events];

    public void Clear() => _events.Clear();

    public void Dispose()
    {
        _allListenersSubscription.Dispose();
        foreach (var subscription in _listenerSubscriptions)
        {
            subscription.Dispose();
        }
    }

    private void OnNewListener(DiagnosticListener listener)
    {
        if (!listener.Name.Contains(_listenerNameContains, StringComparison.Ordinal))
        {
            return;
        }

        var subscription = listener.Subscribe(
            new DelegatingObserver<KeyValuePair<string, object?>>(value => OnEvent(listener.Name, value)));
        _listenerSubscriptions.Add(subscription);
    }

    private void OnEvent(string listenerName, KeyValuePair<string, object?> value)
    {
        var activity = Activity.Current;
        _events.Enqueue(new RecordedDiagnosticEvent(
            listenerName,
            value.Key,
            value.Value,
            activity?.TraceId.ToString(),
            activity?.SpanId.ToString(),
            activity?.OperationName));
    }

    private sealed class DelegatingObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnNext(T value) => onNext(value);

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
