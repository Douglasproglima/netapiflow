using System.Diagnostics;
using System.Runtime.CompilerServices;
using NetApiFlow.Instrumentation;
using NetApiFlow.Sanitization;

namespace NetApiFlow;

/// <summary>A tracked step opened by <see cref="NetApiFlow.Step"/>. Dispose it to close the span.</summary>
public readonly struct FlowStep : IDisposable
{
    // Marks an exception instance as already attributed to the span where it first occurred, so
    // outer layers that catch and rethrow the same instance (controller filters, MediatR behaviors,
    // proxies) record the failure without each claiming to be the origin. Kept outside the
    // exception, in a table keyed by instance identity, because the exception belongs to the
    // caller: anything written to its Data would surface in their logs as a foreign entry.
    private static readonly ConditionalWeakTable<Exception, object> OriginClaimedExceptions = new();

    private static readonly object OriginClaimed = new();

    private readonly Activity? _activity;

    internal FlowStep(Activity? activity)
    {
        _activity = activity;
    }

    /// <summary>Whether this step is backed by no <see cref="Activity"/>, because its trace was not
    /// being tracked when it was opened. Every member is then a no-op.</summary>
    internal bool IsNoOp => _activity is null;

    /// <summary>Attaches an explicit tag to the step.</summary>
    public void Tag(string key, string value) => _activity?.SetTag(key, value);

    /// <summary>Marks the step as failed because of <paramref name="exception"/>.</summary>
    public void MarkError(Exception exception)
    {
        if (_activity is null)
        {
            return;
        }

        var isOrigin = OriginClaimedExceptions.TryAdd(exception, OriginClaimed);

        _activity.SetTag(FlowTagNames.ErrorType, exception.GetType().FullName);
        _activity.SetTag(FlowTagNames.ErrorMessage, ErrorMessageSanitizer.Sanitize(exception.Message));
        _activity.SetTag(FlowTagNames.ErrorOrigin, isOrigin ? bool.TrueString : bool.FalseString);
        _activity.SetStatus(ActivityStatusCode.Error);
    }

    /// <summary>Closes the step.</summary>
    public void Dispose() => _activity?.Dispose();
}
