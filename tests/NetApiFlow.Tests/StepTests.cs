using System.Diagnostics;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests;

public class StepTests
{
    [Fact]
    public void Step_WhenNoListenerIsSubscribed_DoesNotAllocateAnActivity()
    {
        var before = GC.GetAllocatedBytesForCurrentThread();

        using (NetApiFlow.Step("Component", "Operation"))
        {
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).ShouldBeLessThan(200);
    }

    [Fact]
    public void Step_WhenListenerIsSubscribed_SetsComponentOperationAndTypeTags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Instrumentation.FlowActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var step = NetApiFlow.Step("StockService", "ReserveAsync", ComponentType.Service);

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
            ShouldListenTo = source => source.Name == Instrumentation.FlowActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var step = NetApiFlow.Step("Component", "Operation");
        var activity = Activity.Current;
        step.MarkError(new InvalidOperationException("boom"));
        step.Dispose();

        activity.ShouldNotBeNull();
        activity.GetTagItem("netapiflow.error.type").ShouldBe(typeof(InvalidOperationException).FullName);
        activity.GetTagItem("netapiflow.error.message").ShouldBe("boom");
        activity.GetTagItem("netapiflow.error.origin").ShouldBe(bool.TrueString);
        activity.Status.ShouldBe(ActivityStatusCode.Error);
    }
}
