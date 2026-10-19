using System.Diagnostics;
using NetApiFlow.Instrumentation;

namespace NetApiFlow;

/// <summary>A tracked step opened by <see cref="NetApiFlow.Step"/>. Dispose it to close the span.</summary>
public readonly struct FlowStep : IDisposable
{
    private readonly Activity? _activity;

    internal FlowStep(Activity? activity)
    {
        _activity = activity;
    }

    /// <summary>Attaches an explicit tag to the step.</summary>
    public void Tag(string key, string value) => _activity?.SetTag(key, value);

    /// <summary>Marks the step as failed because of <paramref name="exception"/>.</summary>
    public void MarkError(Exception exception)
    {
        if (_activity is null)
        {
            return;
        }

        _activity.SetTag(FlowTagNames.ErrorType, exception.GetType().FullName);
        _activity.SetTag(FlowTagNames.ErrorMessage, exception.Message);
        _activity.SetTag(FlowTagNames.ErrorOrigin, bool.TrueString);
        _activity.SetStatus(ActivityStatusCode.Error);
    }

    /// <summary>Closes the step.</summary>
    public void Dispose() => _activity?.Dispose();
}
