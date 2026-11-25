using NetApiFlow.Domain;
using NetApiFlow.Testing;
using Shouldly;

namespace NetApiFlow.Tests.Testing;

public class FlowTraceAssertionsTests
{
    private static FlowTraceDetail TraceWith(FlowStatus status, params FlowSpan[] spans) => new()
    {
        Trace = new FlowTrace
        {
            TraceId = "t1",
            Method = "GET",
            RouteTemplate = "/api/orders/{id}",
            HttpStatus = status == FlowStatus.Error ? 500 : 200,
            Status = status,
            StartedAt = DateTime.UtcNow,
            EndedAt = DateTime.UtcNow,
            DurationMicros = 1_000,
            SpanCount = spans.Length,
            Truncated = false,
            Tags = new Dictionary<string, string>(),
        },
        Spans = spans,
    };

    private static FlowSpan Span(string component, string operation, ComponentType type = ComponentType.Service, FlowStatus status = FlowStatus.Success) => new()
    {
        TraceId = "t1",
        SpanId = $"{component}.{operation}",
        ParentSpanId = null,
        Sequence = 0,
        Component = component,
        Operation = operation,
        Type = type,
        StartedAt = DateTime.UtcNow,
        EndedAt = DateTime.UtcNow,
        DurationMicros = 100,
        Status = status,
    };

    [Fact]
    public void ShouldHaveStatus_WhenItMatches_ReturnsTheTraceForChaining()
    {
        var trace = TraceWith(FlowStatus.Success);

        trace.ShouldHaveStatus(FlowStatus.Success).ShouldBeSameAs(trace);
    }

    [Fact]
    public void ShouldHaveStatus_WhenItDiffers_ThrowsSayingBothStatuses()
    {
        var trace = TraceWith(FlowStatus.Error);

        var thrown = Should.Throw<FlowAssertionException>(() => trace.ShouldHaveStatus(FlowStatus.Success));

        thrown.Message.ShouldBe("Expected the trace to have status Success, found Error.");
    }

    [Fact]
    public void ShouldHaveNoErrors_WhenNoSpanFailed_ReturnsTheTrace()
    {
        var trace = TraceWith(FlowStatus.Success, Span("OrderService", "GetAsync"));

        trace.ShouldHaveNoErrors().ShouldBeSameAs(trace);
    }

    [Fact]
    public void ShouldHaveNoErrors_WhenASpanFailed_ThrowsNamingIt()
    {
        var trace = TraceWith(
            FlowStatus.Error,
            Span("OrderService", "GetAsync"),
            Span("OrderRepository", "FindAsync", status: FlowStatus.Error));

        var thrown = Should.Throw<FlowAssertionException>(trace.ShouldHaveNoErrors);

        thrown.Message.ShouldBe("Expected no errors, found 1: OrderRepository.FindAsync.");
    }

    [Fact]
    public void ShouldCallAtMost_WhenUnderTheLimit_ReturnsTheTrace()
    {
        var trace = TraceWith(
            FlowStatus.Success,
            Span("Db", "SELECT Orders", ComponentType.Database),
            Span("Db", "SELECT Users", ComponentType.Database));

        trace.ShouldCallAtMost(ComponentType.Database, 2).ShouldBeSameAs(trace);
    }

    [Fact]
    public void ShouldCallAtMost_WhenOverTheLimit_ThrowsWithBothNumbers()
    {
        var trace = TraceWith(
            FlowStatus.Success,
            Span("Db", "SELECT Orders", ComponentType.Database),
            Span("Db", "SELECT Users", ComponentType.Database),
            Span("Db", "SELECT Items", ComponentType.Database));

        var thrown = Should.Throw<FlowAssertionException>(() => trace.ShouldCallAtMost(ComponentType.Database, 2));

        thrown.Message.ShouldBe("Expected at most 2 Database calls, found 3.");
    }

    [Fact]
    public void ShouldCallAtMost_WhenTheLimitIsOne_ThrowsWithTheSingularCall()
    {
        var trace = TraceWith(
            FlowStatus.Success,
            Span("Db", "SELECT Orders", ComponentType.Database),
            Span("Db", "SELECT Users", ComponentType.Database));

        var thrown = Should.Throw<FlowAssertionException>(() => trace.ShouldCallAtMost(ComponentType.Database, 1));

        thrown.Message.ShouldBe("Expected at most 1 Database call, found 2.");
    }

    [Fact]
    public void ShouldCallAtMost_WhenTheLimitIsZero_ThrowsWithThePluralCalls()
    {
        var trace = TraceWith(FlowStatus.Success, Span("Db", "SELECT Orders", ComponentType.Database));

        var thrown = Should.Throw<FlowAssertionException>(() => trace.ShouldCallAtMost(ComponentType.Database, 0));

        thrown.Message.ShouldBe("Expected at most 0 Database calls, found 1.");
    }

    [Fact]
    public void ShouldNeverCall_WhenTheComponentIsAbsent_ReturnsTheTrace()
    {
        var trace = TraceWith(FlowStatus.Success, Span("OrderService", "GetAsync"));

        trace.ShouldNeverCall("PaymentGateway").ShouldBeSameAs(trace);
    }

    [Fact]
    public void ShouldNeverCall_WithoutOperation_MatchesAnyOperationOnTheComponent()
    {
        var trace = TraceWith(FlowStatus.Success, Span("PaymentGateway", "ChargeAsync"));

        var thrown = Should.Throw<FlowAssertionException>(() => trace.ShouldNeverCall("PaymentGateway"));

        thrown.Message.ShouldBe("Expected no call to PaymentGateway, found 1.");
    }

    [Fact]
    public void ShouldNeverCall_WithOperation_IgnoresAnotherOperationOnTheSameComponent()
    {
        var trace = TraceWith(FlowStatus.Success, Span("PaymentGateway", "RefundAsync"));

        trace.ShouldNeverCall("PaymentGateway", "ChargeAsync").ShouldBeSameAs(trace);
    }

    [Fact]
    public void ShouldHaveCalled_WhenPresent_ReturnsTheTrace()
    {
        var trace = TraceWith(FlowStatus.Success, Span("OrderRepository", "FindAsync"));

        trace.ShouldHaveCalled("OrderRepository", "FindAsync").ShouldBeSameAs(trace);
    }

    [Fact]
    public void ShouldHaveCalled_WhenAbsent_ThrowsListingWhatWasRecorded()
    {
        var trace = TraceWith(FlowStatus.Success, Span("OrderService", "GetAsync"));

        var thrown = Should.Throw<FlowAssertionException>(() => trace.ShouldHaveCalled("OrderRepository", "FindAsync"));

        thrown.Message.ShouldBe("Expected a call to OrderRepository.FindAsync, found none. Recorded: OrderService.GetAsync.");
    }

    [Fact]
    public void ShouldHaveCalled_WhenNothingWasRecorded_SaysSoInsteadOfAnEmptyList()
    {
        var trace = TraceWith(FlowStatus.Success);

        var thrown = Should.Throw<FlowAssertionException>(() => trace.ShouldHaveCalled("OrderRepository"));

        thrown.Message.ShouldBe("Expected a call to OrderRepository, found none. Recorded: no spans.");
    }

    [Fact]
    public void Assertions_Chain_SoOneFailureStopsTheChain()
    {
        var trace = TraceWith(
            FlowStatus.Success,
            Span("OrderRepository", "FindAsync", ComponentType.Database));

        trace
            .ShouldHaveStatus(FlowStatus.Success)
            .ShouldHaveNoErrors()
            .ShouldCallAtMost(ComponentType.Database, 1)
            .ShouldHaveCalled("OrderRepository")
            .ShouldNeverCall("PaymentGateway")
            .ShouldBeSameAs(trace);
    }
}
