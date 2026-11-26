using NetApiFlow.Storage;
using NetApiFlow.TestSupport;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Storage;

public class IntervalLogLimiterTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    [Fact]
    public void TryAcquire_TheFirstTime_Allows()
    {
        var subject = new IntervalLogLimiter(Interval, new ManualTimeProvider());

        subject.TryAcquire().ShouldBeTrue();
    }

    [Fact]
    public void TryAcquire_WithinTheInterval_Refuses()
    {
        var clock = new ManualTimeProvider();
        var subject = new IntervalLogLimiter(Interval, clock);
        subject.TryAcquire();

        clock.Advance(Interval - TimeSpan.FromMilliseconds(1));

        subject.TryAcquire().ShouldBeFalse();
    }

    [Fact]
    public void TryAcquire_WhenTheIntervalHasPassed_AllowsAgainAndStartsANewInterval()
    {
        var clock = new ManualTimeProvider();
        var subject = new IntervalLogLimiter(Interval, clock);
        subject.TryAcquire();

        clock.Advance(Interval);

        subject.TryAcquire().ShouldBeTrue();
        subject.TryAcquire().ShouldBeFalse();
    }
}
