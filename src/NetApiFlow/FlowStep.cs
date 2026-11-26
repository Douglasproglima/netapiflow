using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using NetApiFlow.Instrumentation;
using NetApiFlow.Sanitization;

namespace NetApiFlow;

/// <summary>A tracked step opened by <see cref="Flow.Step"/>. Dispose it to close the span.</summary>
public readonly struct FlowStep : IDisposable
{
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

    /// <summary>Attaches an explicit tag to the step. A null key or value is ignored, and a warning is logged.</summary>
    public void Tag(string key, string value)
    {
        if (key is null || value is null)
        {
            NullStepArgumentLog.TagIgnored();
            return;
        }

        _activity?.SetTag(key, value);
    }

    /// <summary>Attaches an explicit tag with a whole number, recorded as text in the invariant culture. A null key is ignored, and a warning is logged.</summary>
    /// <param name="key">The tag name.</param>
    /// <param name="value">The tag value.</param>
    public void Tag(string key, int value) => Tag(key, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Attaches an explicit tag with a whole number, recorded as text in the invariant culture. A null key is ignored, and a warning is logged.</summary>
    /// <param name="key">The tag name.</param>
    /// <param name="value">The tag value.</param>
    public void Tag(string key, long value) => Tag(key, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Attaches an explicit tag with a number, recorded as text in the invariant culture. A null key is ignored, and a warning is logged.</summary>
    /// <param name="key">The tag name.</param>
    /// <param name="value">The tag value.</param>
    public void Tag(string key, double value) => Tag(key, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Attaches an explicit tag with a flag, recorded as <c>true</c> or <c>false</c>. A null key is ignored, and a warning is logged.</summary>
    /// <param name="key">The tag name.</param>
    /// <param name="value">The tag value.</param>
    public void Tag(string key, bool value) => Tag(key, value ? "true" : "false");

    /// <summary>Attaches an explicit tag with an enum value, recorded as the name of its member. A null key is ignored, and a warning is logged.</summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="key">The tag name.</param>
    /// <param name="value">The tag value.</param>
    public void Tag<TEnum>(string key, TEnum value)
        where TEnum : struct, Enum =>
        Tag(key, Enum.GetName(value) ?? value.ToString());

    /// <summary>Marks the step as failed because of <paramref name="exception"/>. A null exception is ignored, and a warning is logged.</summary>
    public void MarkError(Exception exception)
    {
        if (exception is null)
        {
            NullStepArgumentLog.ErrorIgnored();
            return;
        }

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

    internal void MarkFailedWithoutException()
    {
        if (_activity is null)
        {
            return;
        }

        _activity.SetTag(FlowTagNames.ErrorOrigin, bool.TrueString);
        _activity.SetStatus(ActivityStatusCode.Error);
    }

    /// <summary>Closes the step.</summary>
    public void Dispose() => _activity?.Dispose();
}
