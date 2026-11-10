using System.Diagnostics;
using Microsoft.Extensions.Logging;
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
#if NET8_0
    private readonly HttpClientActivityTagBackfill _httpClientActivityTagBackfill;
#endif

    public FlowActivityListener(FlowRecorder recorder, IOptions<NetApiFlowOptions> options, ILoggerFactory loggerFactory)
    {
        _recorder = recorder;
        _options = options;

        _listener = new ActivityListener
        {
            ShouldListenTo = ShouldListenTo,
            Sample = Sample,
            ActivityStarted = OnActivityStarted,
            ActivityStopped = OnActivityStopped,
        };

        ActivitySource.AddActivityListener(_listener);
#if NET8_0
        _httpClientActivityTagBackfill = new HttpClientActivityTagBackfill(options, loggerFactory.CreateLogger<HttpClientActivityTagBackfill>());
#endif
    }

    public void Dispose()
    {
        _listener.Dispose();
#if NET8_0
        _httpClientActivityTagBackfill.Dispose();
#endif
    }

    private bool ShouldListenTo(ActivitySource source) =>
        source.Name == FlowActivitySource.Name ||
        (source.Name == HttpClientActivityTagNames.SourceName &&
            _options.Value.ExternalSources.HasFlag(ExternalActivitySource.HttpClient)) ||
        (source.Name == NpgsqlActivityTagNames.SourceName &&
            _options.Value.ExternalSources.HasFlag(ExternalActivitySource.Npgsql));

    private ActivitySamplingResult Sample(ref ActivityCreationOptions<ActivityContext> options)
    {
        if (!_options.Value.Enabled || IsCommandAlreadyCoveredByAStep(options.Source.Name, Activity.Current))
        {
            return ActivitySamplingResult.None;
        }

        var traceId = options.TraceId.ToString();
        return _recorder.GetActiveTrace(traceId) is null
            ? ActivitySamplingResult.None
            : ActivitySamplingResult.AllDataAndRecorded;
    }

    /// <summary>
    /// A NetApiFlow step wrapping the SQL command already represents it as a span, typically the
    /// one <c>FlowDbCommandInterceptor</c> opens, so the driver's own activity is a duplicate.
    /// Checked while sampling, where dropping it is cheapest, and again once started: any other
    /// <see cref="ActivityListener"/> in the process that samples the same source as recorded
    /// (an OpenTelemetry exporter, for instance) is enough for the activity to be created and
    /// reported to every listener, whatever this one answered while sampling.
    /// </summary>
    private static bool IsCommandAlreadyCoveredByAStep(string sourceName, Activity? step) =>
        sourceName == NpgsqlActivityTagNames.SourceName &&
        step?.Source.Name == FlowActivitySource.Name;

    private void OnActivityStarted(Activity activity)
    {
        if (IsCommandAlreadyCoveredByAStep(activity.Source.Name, activity.Parent))
        {
            return;
        }

        var traceId = activity.TraceId.ToString();
        var trace = _recorder.GetActiveTrace(traceId);
        if (trace is null)
        {
            return;
        }

        var span = activity.Source.Name switch
        {
            HttpClientActivityTagNames.SourceName => BuildHttpClientSpan(activity, traceId, trace),
            NpgsqlActivityTagNames.SourceName => BuildNpgsqlSpan(activity, traceId, trace),
            _ => BuildFlowSpan(activity, traceId, trace),
        };

        _recorder.TryAddSpan(traceId, span);
    }

    private static FlowSpan BuildFlowSpan(Activity activity, string traceId, ActiveTrace trace)
    {
        var componentType = activity.GetTagItem(FlowTagNames.ComponentType) as string;
        var type = componentType is not null && Enum.TryParse<ComponentType>(componentType, out var parsed)
            ? parsed
            : ComponentType.Custom;

        return new FlowSpan
        {
            TraceId = traceId,
            SpanId = activity.SpanId.ToString(),
            ParentSpanId = ResolveParentSpanId(activity, trace),
            Sequence = trace.NextSequence(),
            Component = activity.GetTagItem(FlowTagNames.Component) as string ?? activity.OperationName,
            Operation = activity.GetTagItem(FlowTagNames.Operation) as string ?? activity.OperationName,
            Type = type,
            StartedAt = activity.StartTimeUtc,
            Status = FlowStatus.Running,
        };
    }

    /// <summary>
    /// The real method/host/path only become reliably available in <see cref="OnActivityStopped"/>
    /// (D-69: on <c>net8.0</c> they are backfilled by a <c>HttpClientActivityTagBackfill</c>
    /// subscriber whose ordering relative to this callback is not guaranteed). The span starts with
    /// a generic name and is finalized on completion.
    /// </summary>
    private static FlowSpan BuildHttpClientSpan(Activity activity, string traceId, ActiveTrace trace) =>
        new()
        {
            TraceId = traceId,
            SpanId = activity.SpanId.ToString(),
            ParentSpanId = ResolveParentSpanId(activity, trace),
            Sequence = trace.NextSequence(),
            Component = "HttpClient",
            Operation = "HTTP request",
            Type = ComponentType.Http,
            StartedAt = activity.StartTimeUtc,
            Status = FlowStatus.Running,
        };

    /// <summary>
    /// The command text only lands on the <see cref="Activity"/> while the command runs, so the
    /// span starts with a generic name and is finalized on completion.
    /// </summary>
    private static FlowSpan BuildNpgsqlSpan(Activity activity, string traceId, ActiveTrace trace) =>
        new()
        {
            TraceId = traceId,
            SpanId = activity.SpanId.ToString(),
            ParentSpanId = ResolveParentSpanId(activity, trace),
            Sequence = trace.NextSequence(),
            Component = NpgsqlActivityTagNames.SourceName,
            Operation = "SQL command",
            Type = ComponentType.Database,
            StartedAt = activity.StartTimeUtc,
            Status = FlowStatus.Running,
        };

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

        var (updated, isErrorOrigin) = activity.Source.Name switch
        {
            HttpClientActivityTagNames.SourceName => CompleteHttpClientSpan(activity, existing),
            NpgsqlActivityTagNames.SourceName => CompleteNpgsqlSpan(activity, existing),
            _ => CompleteFlowSpan(activity, existing),
        };

        _recorder.CompleteSpan(traceId, updated);

        if (isErrorOrigin)
        {
            PropagateErrorToAncestors(trace, activity.Parent);
        }
    }

    private static (FlowSpan Span, bool IsErrorOrigin) CompleteFlowSpan(Activity activity, FlowSpan existing)
    {
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

        return (updated, isErrorOrigin);
    }

    /// <summary>
    /// Maps the outbound call's method, status code and error onto the span (D-69, risco
    /// R-13). The URL is reduced to <c>host/path</c> before it ever reaches <see cref="FlowSpan.Operation"/>:
    /// query string and fragment are never recorded. A 4xx/5xx response marks the span as
    /// <see cref="FlowStatus.Error"/>; only a network-level failure (no response at all) marks it as
    /// the error's origin, since an HTTP error status is a result the span itself already reports.
    /// </summary>
    private static (FlowSpan Span, bool IsErrorOrigin) CompleteHttpClientSpan(Activity activity, FlowSpan existing)
    {
        var method = activity.GetTagItem(HttpClientActivityTagNames.Method) as string;
        var sanitizedTarget = OutboundUrlSanitizer.SanitizeHostAndPath(activity.GetTagItem(HttpClientActivityTagNames.UrlFull) as string);
        var statusCode = TryGetStatusCode(activity);
        var errorType = activity.GetTagItem(HttpClientActivityTagNames.ErrorType) as string;

        var isErrorOrigin = statusCode is null && errorType is not null;
        var status = isErrorOrigin || statusCode is >= 400
            ? FlowStatus.Error
            : FlowStatus.Success;

        var updated = existing with
        {
            Operation = method is not null && sanitizedTarget is not null ? $"{method} {sanitizedTarget}" : existing.Operation,
            EndedAt = activity.StartTimeUtc + activity.Duration,
            DurationMicros = (long)activity.Duration.TotalMicroseconds,
            Status = status,
            ErrorType = isErrorOrigin ? errorType : null,
            ErrorMessage = isErrorOrigin
                ? ErrorMessageSanitizer.Sanitize($"Outbound HTTP call to {sanitizedTarget} failed.")
                : null,
            IsErrorOrigin = isErrorOrigin,
        };

        return (updated, isErrorOrigin);
    }

    /// <summary>
    /// Tags are curated, never copied wholesale: the driver also reports connection string,
    /// database user, peer address and port, none of which NetApiFlow's own instrumentation is
    /// allowed to record. The full command text is kept only behind
    /// <see cref="NetApiFlowOptions.CaptureSqlText"/>, the same rule the EF Core interceptor follows.
    /// </summary>
    private (FlowSpan Span, bool IsErrorOrigin) CompleteNpgsqlSpan(Activity activity, FlowSpan existing)
    {
        var statement = activity.GetTagItem(NpgsqlActivityTagNames.Statement) as string;
        var failure = NpgsqlCommandFailure.From(activity);

        var updated = existing with
        {
            Operation = statement is null ? existing.Operation : SqlCommandDescriber.Describe(statement),
            EndedAt = activity.StartTimeUtc + activity.Duration,
            DurationMicros = (long)activity.Duration.TotalMicroseconds,
            Status = failure is null ? FlowStatus.Success : FlowStatus.Error,
            ErrorType = failure?.Type,
            ErrorMessage = ErrorMessageSanitizer.Sanitize(failure?.Message),
            IsErrorOrigin = failure is not null,
            Tags = BuildCapturedSqlTags(statement, existing),
        };

        return (updated, failure is not null);
    }

    private IReadOnlyDictionary<string, string> BuildCapturedSqlTags(string? statement, FlowSpan existing) =>
        _options.Value.CaptureSqlText && statement is not null
            ? new Dictionary<string, string>(StringComparer.Ordinal) { ["sql"] = statement }
            : existing.Tags;

    private static int? TryGetStatusCode(Activity activity) =>
        activity.GetTagItem(HttpClientActivityTagNames.StatusCode) switch
        {
            int statusCode => statusCode,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => null,
        };

    /// <summary>
    /// <see cref="Activity.Parent"/> reflects the real .NET Activity graph, which includes
    /// ambient parents NetApiFlow never recorded as a span (e.g. ASP.NET Core's own request
    /// Activity for the root span of a trace). Only a parent already present in this trace's own
    /// spans is a real NetApiFlow parent; anything else means this span is the trace's root, and
    /// the API contract (docs/plano/contratos.md) requires a root span's <c>parentSpanId</c> to
    /// be <see langword="null"/>, not a dangling id the frontend can never resolve.
    /// </summary>
    private static string? ResolveParentSpanId(Activity activity, ActiveTrace trace)
    {
        var parentSpanId = activity.Parent?.SpanId.ToString();
        return parentSpanId is not null && trace.Spans.Any(span => span.SpanId == parentSpanId)
            ? parentSpanId
            : null;
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
