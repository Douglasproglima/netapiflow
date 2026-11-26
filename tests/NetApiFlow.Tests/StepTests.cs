using System.Diagnostics;
using Shouldly;
using Xunit;
using FlowActivitySource = NetApiFlow.Instrumentation.FlowActivitySource;

namespace NetApiFlow.Tests;

public class StepTests
{
    [Fact]
    public void Step_WhenNoListenerIsSubscribed_DoesNotAllocateAnActivity()
    {
        // Warm up: let the JIT compile Step() once before measuring, so tiered compilation does not
        // show up as allocation noise.
        using (Flow.Step("Component", "Operation"))
        {
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        using (Flow.Step("Component", "Operation"))
        {
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).ShouldBeLessThan(64);
    }

    [Fact]
    public void Step_WhenListenerIsSubscribed_SetsComponentOperationAndTypeTags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FlowActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var step = Flow.Step("StockService", "ReserveAsync", ComponentType.Service);

        var activity = Activity.Current;
        activity.ShouldNotBeNull();
        activity.GetTagItem("netapiflow.component").ShouldBe("StockService");
        activity.GetTagItem("netapiflow.operation").ShouldBe("ReserveAsync");
        activity.GetTagItem("netapiflow.type").ShouldBe("Service");
    }

    [Fact]
    public void MarkError_SetsErrorTagsAndStatus()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FlowActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var step = Flow.Step("Component", "Operation");
        var activity = Activity.Current;
        step.MarkError(new InvalidOperationException("boom"));
        step.Dispose();

        activity.ShouldNotBeNull();
        activity.GetTagItem("netapiflow.error.type").ShouldBe(typeof(InvalidOperationException).FullName);
        activity.GetTagItem("netapiflow.error.message").ShouldBe("boom");
        activity.GetTagItem("netapiflow.error.origin").ShouldBe(bool.TrueString);
        activity.Status.ShouldBe(ActivityStatusCode.Error);
    }

    [Fact]
    public void MarkError_LeavesTheCallerExceptionUntouched()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FlowActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var exception = new InvalidOperationException("boom");

        var step = Flow.Step("Component", "Operation");
        step.MarkError(exception);
        step.Dispose();

        exception.Data.Count.ShouldBe(0);
    }

    [Fact]
    public void MarkError_WhenAnOuterStepMarksTheSameException_OnlyTheInnerStepClaimsOrigin()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FlowActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var exception = new InvalidOperationException("boom");

        var outer = Flow.Step("OuterComponent", "Operation");
        var outerActivity = Activity.Current;
        var inner = Flow.Step("InnerComponent", "Operation");
        var innerActivity = Activity.Current;

        inner.MarkError(exception);
        inner.Dispose();
        outer.MarkError(exception);
        outer.Dispose();

        innerActivity.ShouldNotBeNull();
        outerActivity.ShouldNotBeNull();
        innerActivity.GetTagItem("netapiflow.error.origin").ShouldBe(bool.TrueString);
        outerActivity.GetTagItem("netapiflow.error.origin").ShouldBe(bool.FalseString);
    }
}
