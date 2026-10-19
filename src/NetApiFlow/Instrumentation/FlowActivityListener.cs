using System.Diagnostics;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using NetApiFlow.Sanitization;

namespace NetApiFlow.Instrumentation;

/// <summary>
/// Bridges <see cref="FlowActivitySource"/> activities into the <see cref="FlowRecorder"/>. Decides
/// sampling eagerly (risk R-05): a span is only recorded when tracing is enabled and its trace is
/// already tracked by the recorder.
/// </summary>
internal sealed class FlowActivityListener : IDisposable
{
    private readonly FlowRecorder _recorder;
    private readonly IOptions<NetApiFlowOptions> _options;
    private readonly ActivityListener _listener;

    public FlowActivityListener(FlowRecorder recorder, IOptions<NetApiFlowOptions> options)
    {
        _recorder = recorder;
        _options = options;

        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FlowActivitySource.Name,
            Sample = Sample,
            ActivityStarted = OnActivityStarted,
            ActivityStopped = OnActivityStopped,
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    private ActivitySamplingResult Sample(ref ActivityCreationOptions<ActivityContext> options)
    {
        if (!_options.Value.Enabled)
        {
            return ActivitySamplingResult.None;
        }

        var traceId = options.TraceId.ToString();
        return _recorder.GetActiveTrace(traceId) is null
            ? ActivitySamplingResult.None
            : ActivitySamplingResult.AllDataAndRecorded;
    }

    private void OnActivityStarted(Activity activity)
    {
        var traceId = activity.TraceId.ToString();
        var trace = _recorder.GetActiveTrace(traceId);
        if (trace is null)
        {
            return;
        }

        var componentType = activity.GetTagItem(FlowTagNames.ComponentType) as string;
        var type = componentType is not null && Enum.TryParse<ComponentType>(componentType, out var parsed)
            ? parsed
            : ComponentType.Custom;

        var span = new FlowSpan
        {
            TraceId = traceId,
            SpanId = activity.SpanId.ToString(),
            ParentSpanId = activity.Parent?.SpanId.ToString(),
            Sequence = trace.NextSequence(),
            Component = activity.GetTagItem(FlowTagNames.Component) as string ?? activity.OperationName,
            Operation = activity.GetTagItem(FlowTagNames.Operation) as string ?? activity.OperationName,
            Type = type,
            StartedAt = activity.StartTimeUtc,
            Status = FlowStatus.Running,
        };

        _recorder.TryAddSpan(traceId, span);
    }

    private void OnActivityStopped(Activity activity)
    {
        var traceId = activity.TraceId.ToString();
        var trace = _recorder.GetActiveTrace(traceId);
        if (trace is null)
        {
            return;
        }

        var spanId = activity.SpanId.ToString();
        var existing = trace.Spans.FirstOrDefault(s => s.SpanId == spanId);
        if (existing is null)
        {
            return;
        }

        var isErrorOrigin = (string?)activity.GetTagItem(FlowTagNames.ErrorOrigin) == bool.TrueString;
        var status = isErrorOrigin || existing.Status == FlowStatus.Error
            ? FlowStatus.Error
            : FlowStatus.Success;

        var updated = existing with
        {
            EndedAt = activity.StartTimeUtc + activity.Duration,
            DurationMicros = (long)activity.Duration.TotalMicroseconds,
            Status = status,
            ErrorType = activity.GetTagItem(FlowTagNames.ErrorType) as string,
            ErrorMessage = ErrorMessageSanitizer.Sanitize(activity.GetTagItem(FlowTagNames.ErrorMessage) as string),
            IsErrorOrigin = isErrorOrigin,
        };

        _recorder.CompleteSpan(traceId, updated);

        if (isErrorOrigin)
        {
            PropagateErrorToAncestors(trace, activity.Parent);
        }
    }

    private void PropagateErrorToAncestors(ActiveTrace trace, Activity? parent)
    {
        while (parent is not null)
        {
            var parentSpanId = parent.SpanId.ToString();
            var parentSpan = trace.Spans.FirstOrDefault(s => s.SpanId == parentSpanId);
            if (parentSpan is not null && parentSpan.Status != FlowStatus.Error)
            {
                _recorder.UpdateSpan(trace.TraceId, parentSpan with { Status = FlowStatus.Error });
            }

            parent = parent.Parent;
        }
    }
}
