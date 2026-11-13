using NetApiFlow.Domain;
using NetApiFlow.Storage;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Storage;

public class TailSampleDecisionTests
{
    private static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(250);

    private static FlowTrace BuildTrace(FlowStatus status, long? durationMicros) => new()
    {
        TraceId = "trace-1",
        Method = "GET",
        RouteTemplate = "/api/orders",
        Status = status,
        StartedAt = DateTime.UtcNow,
        DurationMicros = durationMicros,
    };

    [Fact]
    public void ShouldPersist_WhenDisabled_AlwaysReturnsTrue()
    {
        var options = new TailSamplingOptions { Enabled = false, SuccessSampleRate = 0.0 };
        var trace = BuildTrace(FlowStatus.Success, 1_000);

        TailSampleDecision.ShouldPersist(trace, options, SlowThreshold, randomDraw: 0.99).ShouldBeTrue();
    }

    [Fact]
    public void ShouldPersist_WhenTraceIsError_AlwaysReturnsTrue()
    {
        var options = new TailSamplingOptions { Enabled = true, SuccessSampleRate = 0.0 };
        var trace = BuildTrace(FlowStatus.Error, 1_000);

        TailSampleDecision.ShouldPersist(trace, options, SlowThreshold, randomDraw: 0.99).ShouldBeTrue();
    }

    [Fact]
    public void ShouldPersist_WhenTraceIsTimeout_AlwaysReturnsTrue()
    {
        var options = new TailSamplingOptions { Enabled = true, SuccessSampleRate = 0.0 };
        var trace = BuildTrace(FlowStatus.Timeout, 1_000);

        TailSampleDecision.ShouldPersist(trace, options, SlowThreshold, randomDraw: 0.99).ShouldBeTrue();
    }

    [Fact]
    public void ShouldPersist_WhenTraceIsCancelled_FollowsTheSameSamplingAsSuccess()
    {
        var options = new TailSamplingOptions { Enabled = true, SuccessSampleRate = 0.1 };
        var trace = BuildTrace(FlowStatus.Cancelled, 1_000);

        TailSampleDecision.ShouldPersist(trace, options, SlowThreshold, randomDraw: 0.5).ShouldBeFalse();
    }

    [Fact]
    public void ShouldPersist_WhenDurationAtSlowThreshold_ReturnsTrue()
    {
        var options = new TailSamplingOptions { Enabled = true, SuccessSampleRate = 0.0 };
        var trace = BuildTrace(FlowStatus.Success, (long)SlowThreshold.TotalMicroseconds);

        TailSampleDecision.ShouldPersist(trace, options, SlowThreshold, randomDraw: 0.99).ShouldBeTrue();
    }

    [Fact]
    public void ShouldPersist_WhenDurationBelowSlowThreshold_DoesNotCountAsSlow()
    {
        var options = new TailSamplingOptions { Enabled = true, SuccessSampleRate = 0.5 };
        var trace = BuildTrace(FlowStatus.Success, (long)SlowThreshold.TotalMicroseconds - 1);

        TailSampleDecision.ShouldPersist(trace, options, SlowThreshold, randomDraw: 0.6).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0.09, true)]
    [InlineData(0.1, false)]
    [InlineData(0.5, false)]
    public void ShouldPersist_WhenFastSuccess_ComparesRandomDrawToSuccessSampleRate(double randomDraw, bool expected)
    {
        var options = new TailSamplingOptions { Enabled = true, SuccessSampleRate = 0.1 };
        var trace = BuildTrace(FlowStatus.Success, 1_000);

        TailSampleDecision.ShouldPersist(trace, options, SlowThreshold, randomDraw).ShouldBe(expected);
    }

    [Fact]
    public void ShouldPersist_WhenSuccessSampleRateIsZero_NeverPersistsFastSuccess()
    {
        var options = new TailSamplingOptions { Enabled = true, SuccessSampleRate = 0.0 };
        var trace = BuildTrace(FlowStatus.Success, 1_000);

        TailSampleDecision.ShouldPersist(trace, options, SlowThreshold, randomDraw: 0.0).ShouldBeFalse();
    }

    [Fact]
    public void ShouldPersist_WhenSuccessSampleRateIsOne_AlwaysPersistsFastSuccess()
    {
        var options = new TailSamplingOptions { Enabled = true, SuccessSampleRate = 1.0 };
        var trace = BuildTrace(FlowStatus.Success, 1_000);

        TailSampleDecision.ShouldPersist(trace, options, SlowThreshold, randomDraw: 0.999).ShouldBeTrue();
    }
}
