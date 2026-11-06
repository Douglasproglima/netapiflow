using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Instrumentation;

public class FlowLoggerTests
{
    private static (FlowRecorder recorder, FlowActivityListener listener, FlowLogger subject, string traceId, Activity root) Arrange(
        LogLevel minLevel = LogLevel.Warning, int maxLogEntriesPerSpan = 20)
    {
        var options = new NetApiFlowOptions();
        options.LogCorrelation.MinLevel = minLevel;
        options.Limits.MaxLogEntriesPerSpan = maxLogEntriesPerSpan;
        var wrappedOptions = Options.Create(options);
        var recorder = new FlowRecorder(wrappedOptions);
        var listener = new FlowActivityListener(recorder, wrappedOptions, NullLoggerFactory.Instance);
        var subject = new FlowLogger(recorder, wrappedOptions);

        var root = new Activity("TestRoot");
        root.Start();
        var traceId = root.TraceId.ToString();
        recorder.TryStartTrace(traceId, "GET", "/api/test", DateTime.UtcNow);

        return (recorder, listener, subject, traceId, root);
    }

    [Fact]
    public void Log_BelowMinLevel_DoesNotCorrelateToSpan()
    {
        var (recorder, listener, subject, traceId, root) = Arrange(minLevel: LogLevel.Warning);
        using var _ = listener;
        using var __ = root;

        using (NetApiFlow.Step("Component", "Operation"))
        {
            subject.Log(LogLevel.Information, new EventId(0), "state", null, (s, e) => s);
        }

        recorder.GetActiveTrace(traceId)!.Spans.Single().Logs.ShouldBeEmpty();
    }

    [Fact]
    public void Log_OutsideAFlowActivitySource_DoesNotCorrelateAndDoesNotThrow()
    {
        var (recorder, listener, subject, traceId, root) = Arrange();
        using var _ = listener;
        using var __ = root;
        using var otherSource = new ActivitySource("SomeOtherSource");

        Should.NotThrow(() =>
        {
            using (otherSource.StartActivity("unrelated"))
            {
                subject.Log(LogLevel.Error, new EventId(0), "state", null, (s, e) => s);
            }
        });

        recorder.GetActiveTrace(traceId)!.Spans.ShouldBeEmpty();
    }

    [Fact]
    public void Log_WithoutActivityCurrent_DoesNotCorrelateAndDoesNotThrow()
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        var subject = new FlowLogger(recorder, options);

        Should.NotThrow(() => subject.Log(LogLevel.Error, new EventId(0), "state", null, (s, e) => s));
    }

    [Fact]
    public void Log_WithinAFlowStep_CorrelatesToTheActiveSpan()
    {
        var (recorder, listener, subject, traceId, root) = Arrange();
        using var _ = listener;
        using var __ = root;

        using (NetApiFlow.Step("Component", "Operation"))
        {
            subject.Log(LogLevel.Warning, new EventId(0), "state", null, (s, e) => "something happened");
        }

        var entry = recorder.GetActiveTrace(traceId)!.Spans.Single().Logs.Single();
        entry.Level.ShouldBe("Warning");
        entry.Message.ShouldBe("something happened");
    }

    [Fact]
    public void Log_WithException_PopulatesExceptionTypeAndMessage()
    {
        var (recorder, listener, subject, traceId, root) = Arrange();
        using var _ = listener;
        using var __ = root;

        using (NetApiFlow.Step("Component", "Operation"))
        {
            subject.Log(LogLevel.Error, new EventId(0), "state", new InvalidOperationException("boom"), (s, e) => "failed");
        }

        var entry = recorder.GetActiveTrace(traceId)!.Spans.Single().Logs.Single();
        entry.ExceptionType.ShouldBe(typeof(InvalidOperationException).FullName);
        entry.ExceptionMessage.ShouldBe("boom");
    }

    [Fact]
    public void Log_WithSensitiveDataInMessage_SanitizesBeforeStoring()
    {
        var (recorder, listener, subject, traceId, root) = Arrange();
        using var _ = listener;
        using var __ = root;

        using (NetApiFlow.Step("Component", "Operation"))
        {
            subject.Log(LogLevel.Warning, new EventId(0), "state", null, (s, e) => "contact someone@example.com, cpf 123.456.789-09");
        }

        var entry = recorder.GetActiveTrace(traceId)!.Spans.Single().Logs.Single();
        entry.Message.ShouldNotContain("someone@example.com");
        entry.Message.ShouldContain("[redacted-email]");
        entry.Message.ShouldContain("[redacted-cpf]");
    }

    [Fact]
    public void Log_BeyondMaxLogEntriesPerSpan_DropsExtraEntriesAndCountsThem()
    {
        var (recorder, listener, subject, traceId, root) = Arrange(maxLogEntriesPerSpan: 2);
        using var _ = listener;
        using var __ = root;

        using (NetApiFlow.Step("Component", "Operation"))
        {
            subject.Log(LogLevel.Warning, new EventId(0), "state", null, (s, e) => "one");
            subject.Log(LogLevel.Warning, new EventId(0), "state", null, (s, e) => "two");
            subject.Log(LogLevel.Warning, new EventId(0), "state", null, (s, e) => "three");
        }

        var span = recorder.GetActiveTrace(traceId)!.Spans.Single();
        span.Logs.Count.ShouldBe(2);
        recorder.DroppedLogCount.ShouldBe(1);
    }
}
