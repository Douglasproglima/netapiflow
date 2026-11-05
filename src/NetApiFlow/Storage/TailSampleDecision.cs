using NetApiFlow.Domain;

namespace NetApiFlow.Storage;

/// <summary>Pure decision of whether a completed trace should be persisted under tail-based
/// sampling (D-82). The random draw is a parameter, not an internal <see cref="Random"/>, so the
/// decision is deterministic and testable.</summary>
internal static class TailSampleDecision
{
    /// <summary>Returns <see langword="true"/> when <paramref name="trace"/> should be persisted.</summary>
    /// <param name="trace">The completed trace.</param>
    /// <param name="options">The configured tail-sampling options.</param>
    /// <param name="slowThreshold">The duration at or above which a trace is always persisted.</param>
    /// <param name="randomDraw">A value in <c>[0.0, 1.0)</c>, normally <see cref="Random.NextDouble"/>.</param>
    public static bool ShouldPersist(FlowTrace trace, TailSamplingOptions options, TimeSpan slowThreshold, double randomDraw)
    {
        if (!options.Enabled)
        {
            return true;
        }

        if (trace.Status == FlowStatus.Error)
        {
            return true;
        }

        if (trace.DurationMicros is { } durationMicros && durationMicros >= slowThreshold.TotalMicroseconds)
        {
            return true;
        }

        return randomDraw < options.SuccessSampleRate;
    }
}
