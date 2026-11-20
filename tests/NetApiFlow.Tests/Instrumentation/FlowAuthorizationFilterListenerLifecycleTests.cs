using Microsoft.Extensions.Logging.Abstractions;
using NetApiFlow.Instrumentation;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Instrumentation;

public class FlowAuthorizationFilterListenerLifecycleTests
{
    private static FlowAuthorizationFilterListener CreateSubject() =>
        new(NullLogger<FlowAuthorizationFilterListener>.Instance);

    [Fact]
    public void Constructor_WithoutStart_DoesNotSubscribe()
    {
        using var subject = CreateSubject();

        subject.IsSubscribed.ShouldBeFalse();
    }

    [Fact]
    public void Start_CalledTwice_SubscribesOnce()
    {
        using var subject = CreateSubject();

        subject.Start();
        subject.Start();

        subject.IsSubscribed.ShouldBeTrue();
    }

    [Fact]
    public void Start_AfterDispose_DoesNotSubscribe()
    {
        var subject = CreateSubject();
        subject.Dispose();

        subject.Start();

        subject.IsSubscribed.ShouldBeFalse();
    }

    [Fact]
    public void Dispose_WithoutStart_DoesNotThrow()
    {
        var subject = CreateSubject();

        Should.NotThrow(subject.Dispose);
    }

    [Fact]
    public void CloseOrphanedSteps_WhileNotSubscribed_ReturnsWithoutThrowing()
    {
        using var subject = CreateSubject();

        Should.NotThrow(() => subject.CloseOrphanedSteps("trace", null));
    }
}
