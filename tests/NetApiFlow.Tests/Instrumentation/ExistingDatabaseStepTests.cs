using System.Diagnostics;
using NetApiFlow.Instrumentation;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Instrumentation;

public class ExistingDatabaseStepTests : IDisposable
{
    private readonly ActivityListener _listener = new()
    {
        ShouldListenTo = _ => true,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    };

    public ExistingDatabaseStepTests() => ActivitySource.AddActivityListener(_listener);

    public void Dispose()
    {
        _listener.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<ComponentType, bool> EveryComponentType()
    {
        var data = new TheoryData<ComponentType, bool>();
        foreach (var type in Enum.GetValues<ComponentType>())
        {
            data.Add(type, type == ComponentType.Database);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryComponentType))]
    public void Covers_ForAFlowStepOfEachType_IsTrueOnlyForDatabase(ComponentType type, bool expected)
    {
        using var step = Flow.Step("Component", "Operation", type);

        ExistingDatabaseStep.Covers(Activity.Current).ShouldBe(expected);
    }

    [Fact]
    public void Covers_WithNoActivity_IsFalse()
    {
        ExistingDatabaseStep.Covers(null).ShouldBeFalse();
    }

    [Fact]
    public void Covers_ForAnActivityFromAnotherSourceTaggedAsDatabase_IsFalse()
    {
        using var source = new ActivitySource("Another.Source");
        using var activity = source.StartActivity("work");
        activity!.SetTag(FlowTagNames.ComponentType, "Database");

        ExistingDatabaseStep.Covers(activity).ShouldBeFalse();
    }

    [Fact]
    public void Covers_ForAFlowActivityWithoutTheTypeTag_IsFalse()
    {
        using var activity = FlowActivitySource.Source.StartActivity("untagged");

        ExistingDatabaseStep.Covers(activity).ShouldBeFalse();
    }
}
