using NetApiFlow.Domain;

namespace NetApiFlow.Testing;

/// <summary>
/// Assertions over a trace a test just produced, so an expectation about the flow itself ("this
/// endpoint must not hit the database more than twice") is written in the test rather than
/// inspected by eye in the dashboard. Each one returns the trace, so they chain.
/// </summary>
public static class FlowTraceAssertions
{
    /// <summary>Asserts the trace finished with <paramref name="status"/>.</summary>
    /// <exception cref="FlowAssertionException">The trace has a different status.</exception>
    public static FlowTraceDetail ShouldHaveStatus(this FlowTraceDetail trace, FlowStatus status)
    {
        ArgumentNullException.ThrowIfNull(trace);

        if (trace.Trace.Status != status)
        {
            throw new FlowAssertionException(
                $"Expected the trace to have status {status}, found {trace.Trace.Status}.");
        }

        return trace;
    }

    /// <summary>Asserts no span in the trace failed, and neither did the trace itself.</summary>
    /// <exception cref="FlowAssertionException">The trace or any of its spans is in error.</exception>
    public static FlowTraceDetail ShouldHaveNoErrors(this FlowTraceDetail trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        var failed = trace.Spans.Where(span => span.Status == FlowStatus.Error).ToList();
        if (failed.Count > 0)
        {
            throw new FlowAssertionException(
                $"Expected no errors, found {failed.Count}: {string.Join(", ", failed.Select(Describe))}.");
        }

        if (trace.Trace.Status == FlowStatus.Error)
        {
            throw new FlowAssertionException(
                "Expected no errors, but the trace itself is in error with no span marked as the origin.");
        }

        return trace;
    }

    /// <summary>Asserts the trace has at most <paramref name="maxCount"/> spans of <paramref name="type"/>.</summary>
    /// <exception cref="FlowAssertionException">More spans of that type were recorded.</exception>
    public static FlowTraceDetail ShouldCallAtMost(this FlowTraceDetail trace, ComponentType type, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(trace);

        var found = trace.Spans.Count(span => span.Type == type);
        if (found > maxCount)
        {
            throw new FlowAssertionException($"Expected at most {maxCount} {type} {(maxCount == 1 ? "call" : "calls")}, found {found}.");
        }

        return trace;
    }

    /// <summary>
    /// Asserts the trace never called <paramref name="component"/>. With <paramref name="operation"/>
    /// left null, any operation on that component counts; otherwise only that exact pair.
    /// </summary>
    /// <exception cref="FlowAssertionException">A matching span was recorded.</exception>
    public static FlowTraceDetail ShouldNeverCall(this FlowTraceDetail trace, string component, string? operation = null)
    {
        ArgumentNullException.ThrowIfNull(trace);

        var matches = trace.Spans.Where(span => Matches(span, component, operation)).ToList();
        if (matches.Count > 0)
        {
            throw new FlowAssertionException(
                $"Expected no call to {Describe(component, operation)}, found {matches.Count}.");
        }

        return trace;
    }

    /// <summary>
    /// Asserts the trace called <paramref name="component"/> at least once. With
    /// <paramref name="operation"/> left null, any operation on that component counts; otherwise
    /// only that exact pair.
    /// </summary>
    /// <exception cref="FlowAssertionException">No matching span was recorded.</exception>
    public static FlowTraceDetail ShouldHaveCalled(this FlowTraceDetail trace, string component, string? operation = null)
    {
        ArgumentNullException.ThrowIfNull(trace);

        if (!trace.Spans.Any(span => Matches(span, component, operation)))
        {
            throw new FlowAssertionException(
                $"Expected a call to {Describe(component, operation)}, found none. Recorded: {DescribeRecorded(trace)}.");
        }

        return trace;
    }

    private static bool Matches(FlowSpan span, string component, string? operation) =>
        string.Equals(span.Component, component, StringComparison.Ordinal) &&
        (operation is null || string.Equals(span.Operation, operation, StringComparison.Ordinal));

    private static string Describe(FlowSpan span) => $"{span.Component}.{span.Operation}";

    private static string Describe(string component, string? operation) =>
        operation is null ? component : $"{component}.{operation}";

    private static string DescribeRecorded(FlowTraceDetail trace) =>
        trace.Spans.Count == 0 ? "no spans" : string.Join(", ", trace.Spans.Select(Describe));
}
