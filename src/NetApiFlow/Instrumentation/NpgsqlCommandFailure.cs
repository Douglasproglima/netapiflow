using System.Diagnostics;

namespace NetApiFlow.Instrumentation;

/// <summary>
/// How a Npgsql command activity failed. The driver leaves <see cref="Activity.Status"/> unset and
/// reports the outcome through the older OpenTelemetry convention instead, an "otel.status_code"
/// tag plus an "exception" event. The native status is read first, so this keeps working unchanged
/// once the driver moves to the current convention.
/// </summary>
internal sealed class NpgsqlCommandFailure
{
    private const string LegacyStatusCodeTag = "otel.status_code";
    private const string LegacyErrorStatusCode = "ERROR";
    private const string ExceptionEventName = "exception";
    private const string ExceptionTypeTag = "exception.type";
    private const string ExceptionMessageTag = "exception.message";

    private NpgsqlCommandFailure(string? type, string? message)
    {
        Type = type;
        Message = message;
    }

    public string? Type { get; }

    public string? Message { get; }

    public static NpgsqlCommandFailure? From(Activity activity)
    {
        if (!HasFailed(activity))
        {
            return null;
        }

        foreach (var activityEvent in activity.Events)
        {
            if (activityEvent.Name == ExceptionEventName)
            {
                return new NpgsqlCommandFailure(
                    TagOf(activityEvent, ExceptionTypeTag),
                    TagOf(activityEvent, ExceptionMessageTag) ?? activity.StatusDescription);
            }
        }

        return new NpgsqlCommandFailure(null, activity.StatusDescription);
    }

    private static bool HasFailed(Activity activity) =>
        activity.Status == ActivityStatusCode.Error ||
        activity.GetTagItem(LegacyStatusCodeTag) as string == LegacyErrorStatusCode;

    private static string? TagOf(ActivityEvent activityEvent, string key)
    {
        foreach (var tag in activityEvent.Tags)
        {
            if (tag.Key == key)
            {
                return tag.Value as string;
            }
        }

        return null;
    }
}
