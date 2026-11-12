using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Instrumentation;

public class FlowActivityListenerTests
{
    private static (FlowRecorder recorder, FlowActivityListener subject, string traceId, Activity root) Arrange(
        NetApiFlowOptions? options = null)
    {
        var wrappedOptions = Options.Create(options ?? new NetApiFlowOptions());
        var recorder = new FlowRecorder(wrappedOptions);
        var subject = new FlowActivityListener(recorder, wrappedOptions, NullLoggerFactory.Instance);

        var root = new Activity("TestRoot");
        root.Start();
        var traceId = root.TraceId.ToString();
        recorder.TryStartTrace(traceId, "GET", "/api/test", DateTime.UtcNow);

        return (recorder, subject, traceId, root);
    }

    [Fact]
    public void Sample_WhenTracingIsEnabledAndTraceIsTracked_RecordsSpanWithComponentOperationAndType()
    {
        var (recorder, subject, traceId, root) = Arrange();
        using var _ = subject;
        using var __ = root;

        using (NetApiFlow.Step("StockService", "ReserveAsync", ComponentType.Service))
        {
        }

        var trace = recorder.GetActiveTrace(traceId);
        trace.ShouldNotBeNull();
        trace.Spans.Count.ShouldBe(1);
        var span = trace.Spans[0];
        span.Component.ShouldBe("StockService");
        span.Operation.ShouldBe("ReserveAsync");
        span.Type.ShouldBe(ComponentType.Service);
        span.Status.ShouldBe(FlowStatus.Success);
        span.DurationMicros.ShouldNotBeNull();
    }

    [Fact]
    public void ActivityStarted_WithExternalSourcesHttpClient_ObservesSystemNetHttpActivityAsASpan()
    {
        var (recorder, subject, traceId, root) = Arrange(new NetApiFlowOptions { ExternalSources = ExternalActivitySource.HttpClient });
        using var _ = subject;
        using var __ = root;
        using var httpClientSource = new ActivitySource(HttpClientActivityTagNames.SourceName);

        using (httpClientSource.StartActivity("System.Net.Http.HttpRequestOut"))
        {
        }

        recorder.GetActiveTrace(traceId)!.Spans.Count.ShouldBe(1);
    }

    [Fact]
    public void ActivityStarted_WithDefaultExternalSources_DoesNotObserveSystemNetHttpActivity()
    {
        var (recorder, subject, traceId, root) = Arrange();
        using var _ = subject;
        using var __ = root;
        using var httpClientSource = new ActivitySource(HttpClientActivityTagNames.SourceName);

        using (httpClientSource.StartActivity("System.Net.Http.HttpRequestOut"))
        {
        }

        recorder.GetActiveTrace(traceId)!.Spans.ShouldBeEmpty();
    }

    [Fact]
    public void Sample_WhenDisabled_ReturnsNoneAndDoesNotRecordASpan()
    {
        var (recorder, subject, traceId, root) = Arrange(new NetApiFlowOptions { Enabled = false });
        using var _ = subject;
        using var __ = root;

        // Warm up: let the JIT compile Step() once before measuring.
        using (NetApiFlow.Step("Component", "Operation"))
        {
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        using (NetApiFlow.Step("Component", "Operation"))
        {
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        // No Activity, FlowSpan or recorder update happens on this path; the small residual
        // allocation is only the tags collection built before Sample rejects the span.
        (after - before).ShouldBeLessThan(320);
        recorder.GetActiveTrace(traceId)!.Spans.ShouldBeEmpty();
    }

    [Fact]
    public void OnActivityStarted_RootSpan_RecordsNullParentEvenWithAnAmbientNonFlowParentActivity()
    {
        // `root` mimics ASP.NET Core's own request Activity: a real ambient parent, but never
        // recorded as a NetApiFlow span (different ActivitySource, not listened to). The contract
        // (docs/plano/contratos.md) requires the trace's root span to report `parentSpanId: null`
        // regardless, so the frontend's root-detection (`parentSpanId === null`) always finds one.
        var (recorder, subject, traceId, root) = Arrange();
        using var _ = subject;
        using var __ = root;

        using (NetApiFlow.Step("OrdersController", "Get", ComponentType.Controller))
        {
        }

        var span = recorder.GetActiveTrace(traceId)!.Spans.Single();
        span.ParentSpanId.ShouldBeNull();
    }

    [Fact]
    public void OnActivityStarted_NestedSpan_RecordsTheRealNetApiFlowParent()
    {
        var (recorder, subject, traceId, root) = Arrange();
        using var _ = subject;
        using var __ = root;

        using (NetApiFlow.Step("Outer", "Op", ComponentType.Service))
        {
            using (NetApiFlow.Step("Inner", "Op", ComponentType.Repository))
            {
            }
        }

        var spans = recorder.GetActiveTrace(traceId)!.Spans;
        var outer = spans.Single(s => s.Component == "Outer");
        var inner = spans.Single(s => s.Component == "Inner");

        outer.ParentSpanId.ShouldBeNull();
        inner.ParentSpanId.ShouldBe(outer.SpanId);
    }

    [Fact]
    public void ActivityStopped_WhenDescendantSpanErrors_MarksAncestorsErrorWithoutIsErrorOrigin()
    {
        var (recorder, subject, traceId, root) = Arrange();
        using var _ = subject;
        using var __ = root;

        using (NetApiFlow.Step("Outer", "Op", ComponentType.Service))
        {
            var inner = NetApiFlow.Step("Inner", "Op", ComponentType.Repository);
            inner.MarkError(new InvalidOperationException("boom"));
            inner.Dispose();
        }

        var spans = recorder.GetActiveTrace(traceId)!.Spans;
        var outerSpan = spans.Single(s => s.Component == "Outer");
        var innerSpan = spans.Single(s => s.Component == "Inner");

        innerSpan.Status.ShouldBe(FlowStatus.Error);
        innerSpan.IsErrorOrigin.ShouldBeTrue();
        outerSpan.Status.ShouldBe(FlowStatus.Error);
        outerSpan.IsErrorOrigin.ShouldBeFalse();
    }

    [Fact]
    public void ActivityStopped_WithCustomTag_CopiesItToSpanTagsAndExcludesReservedNetApiFlowTags()
    {
        var (recorder, subject, traceId, root) = Arrange();
        using var _ = subject;
        using var __ = root;

        using (var step = NetApiFlow.Step("OrdersService", "Reserve", ComponentType.Service))
        {
            step.Tag("order_id", "123");
        }

        var span = recorder.GetActiveTrace(traceId)!.Spans.Single();
        span.Tags["order_id"].ShouldBe("123");
        span.Tags.Keys.ShouldNotContain(key => key.StartsWith("netapiflow.", StringComparison.Ordinal));
    }

    [Fact]
    public void ActivityStopped_WithTagValueLongerThanMaxTagValueLength_TruncatesTheValue()
    {
        var (recorder, subject, traceId, root) = Arrange();
        using var _ = subject;
        using var __ = root;
        var longValue = new string('a', 5000);

        using (var step = NetApiFlow.Step("Component", "Operation"))
        {
            step.Tag("payload", longValue);
        }

        var span = recorder.GetActiveTrace(traceId)!.Spans.Single();
        span.Tags["payload"].ShouldBe(longValue[..4000]);
    }

    [Fact]
    public void ActivityStopped_WithMoreTagsThanMaxTagsPerSpan_KeepsOnlyTheFirstTagsInArrivalOrder()
    {
        var (recorder, subject, traceId, root) = Arrange();
        using var _ = subject;
        using var __ = root;

        using (var step = NetApiFlow.Step("Component", "Operation"))
        {
            for (var i = 0; i < 25; i++)
            {
                step.Tag($"tag-{i}", i.ToString(CultureInfo.InvariantCulture));
            }
        }

        var span = recorder.GetActiveTrace(traceId)!.Spans.Single();
        span.Tags.Count.ShouldBe(20);
        span.Tags.Keys.ShouldContain("tag-0");
        span.Tags.Keys.ShouldNotContain("tag-24");
    }

    [Fact]
    public void ActivityStopped_ComparedToASpanWithACustomTag_TheNoTagCaseAllocatesLess()
    {
        var (_, subject, _, root) = Arrange();
        using var __ = subject;
        using var ___ = root;

        // Warm up: let the JIT compile both paths once before measuring.
        using (NetApiFlow.Step("Component", "Operation"))
        {
        }

        using (var warmupStep = NetApiFlow.Step("Component", "Operation"))
        {
            warmupStep.Tag("order_id", "123");
        }

        var beforeNoTag = GC.GetAllocatedBytesForCurrentThread();
        using (NetApiFlow.Step("Component", "Operation"))
        {
        }

        var noTagAllocation = GC.GetAllocatedBytesForCurrentThread() - beforeNoTag;

        var beforeWithTag = GC.GetAllocatedBytesForCurrentThread();
        using (var step = NetApiFlow.Step("Component", "Operation"))
        {
            step.Tag("order_id", "123");
        }

        var withTagAllocation = GC.GetAllocatedBytesForCurrentThread() - beforeWithTag;

        noTagAllocation.ShouldBeLessThan(withTagAllocation);
    }

    [Fact]
    public void Sample_WithNpgsqlActivityUnderAnOpenStep_DropsItAsADuplicate()
    {
        var (recorder, subject, traceId, root) = Arrange(new NetApiFlowOptions { ExternalSources = ExternalActivitySource.Npgsql });
        using var _ = subject;
        using var __ = root;
        using var npgsqlSource = new ActivitySource(NpgsqlActivityTagNames.SourceName);

        using (NetApiFlow.Step("EntityFrameworkCore", "SELECT Users", ComponentType.Database))
        {
            using var npgsqlActivity = npgsqlSource.StartActivity("postgres");
            npgsqlActivity.ShouldBeNull();
        }

        var spans = recorder.GetActiveTrace(traceId)!.Spans;
        spans.Count.ShouldBe(1);
        spans[0].Component.ShouldBe("EntityFrameworkCore");
    }

    [Fact]
    public void Sample_WithNpgsqlActivityUnderAnOpenStepAndAnotherRecordingListener_StillDropsTheDuplicate()
    {
        var (recorder, subject, traceId, root) = Arrange(new NetApiFlowOptions { ExternalSources = ExternalActivitySource.Npgsql });
        using var _ = subject;
        using var __ = root;
        using var otherListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NpgsqlActivityTagNames.SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(otherListener);
        using var npgsqlSource = new ActivitySource(NpgsqlActivityTagNames.SourceName);

        using (NetApiFlow.Step("EntityFrameworkCore", "SELECT Users", ComponentType.Database))
        {
            using var npgsqlActivity = npgsqlSource.StartActivity("postgres");
            npgsqlActivity?.SetTag(NpgsqlActivityTagNames.Statement, "SELECT u.\"Id\" FROM \"Users\" AS u");
        }

        var spans = recorder.GetActiveTrace(traceId)!.Spans;
        spans.Count.ShouldBe(1);
        spans[0].Component.ShouldBe("EntityFrameworkCore");
    }

    [Fact]
    public void Sample_WithNpgsqlActivityWithoutAnOpenStep_RecordsADatabaseSpan()
    {
        var (recorder, subject, traceId, root) = Arrange(new NetApiFlowOptions { ExternalSources = ExternalActivitySource.Npgsql });
        using var _ = subject;
        using var __ = root;
        using var npgsqlSource = new ActivitySource(NpgsqlActivityTagNames.SourceName);

        using (var npgsqlActivity = npgsqlSource.StartActivity("postgres"))
        {
            npgsqlActivity.ShouldNotBeNull();
            npgsqlActivity.SetTag(NpgsqlActivityTagNames.Statement, "SELECT u.\"Id\" FROM \"Users\" AS u WHERE u.\"Email\" = $1");
        }

        var spans = recorder.GetActiveTrace(traceId)!.Spans;
        spans.Count.ShouldBe(1);
        spans[0].Component.ShouldBe("Npgsql");
        spans[0].Operation.ShouldBe("SELECT Users");
        spans[0].Type.ShouldBe(ComponentType.Database);
        spans[0].Tags.Keys.ShouldNotContain("sql");
    }

    [Fact]
    public void ActivityStopped_WithNpgsqlActivityAndCaptureSqlText_RecordsTheCommandTextAsATag()
    {
        const string statement = "SELECT u.\"Id\" FROM \"Users\" AS u WHERE u.\"Email\" = $1";
        var (recorder, subject, traceId, root) = Arrange(new NetApiFlowOptions
        {
            ExternalSources = ExternalActivitySource.Npgsql,
            CaptureSqlText = true,
        });
        using var _ = subject;
        using var __ = root;
        using var npgsqlSource = new ActivitySource(NpgsqlActivityTagNames.SourceName);

        using (var npgsqlActivity = npgsqlSource.StartActivity("postgres"))
        {
            npgsqlActivity!.SetTag(NpgsqlActivityTagNames.Statement, statement);
            npgsqlActivity.SetTag("db.connection_string", "Host=127.0.0.1;Port=5432;Database=app;Username=postgres");
            npgsqlActivity.SetTag("db.user", "postgres");
            npgsqlActivity.SetTag("net.peer.ip", "127.0.0.1");
        }

        var span = recorder.GetActiveTrace(traceId)!.Spans.Single();
        span.Tags["sql"].ShouldBe(statement);
        span.Tags.Keys.ShouldBe(["sql"]);
    }

    [Fact]
    public void ActivityStopped_WithFailedNpgsqlActivity_MarksTheSpanAsTheErrorOrigin()
    {
        var (recorder, subject, traceId, root) = Arrange(new NetApiFlowOptions { ExternalSources = ExternalActivitySource.Npgsql });
        using var _ = subject;
        using var __ = root;
        using var npgsqlSource = new ActivitySource(NpgsqlActivityTagNames.SourceName);

        using (var npgsqlActivity = npgsqlSource.StartActivity("postgres"))
        {
            npgsqlActivity!.SetTag(NpgsqlActivityTagNames.Statement, "SELECT * FROM \"Missing\"");
            npgsqlActivity.SetStatus(ActivityStatusCode.Error, "relation \"Missing\" does not exist");
        }

        var span = recorder.GetActiveTrace(traceId)!.Spans.Single();
        span.Status.ShouldBe(FlowStatus.Error);
        span.IsErrorOrigin.ShouldBeTrue();
        span.ErrorMessage.ShouldBe("relation \"Missing\" does not exist");
    }

    [Fact]
    public void ActivityStopped_WithFailedNpgsqlActivityUsingTheLegacyConvention_MarksTheSpanAsTheErrorOrigin()
    {
        var (recorder, subject, traceId, root) = Arrange(new NetApiFlowOptions { ExternalSources = ExternalActivitySource.Npgsql });
        using var _ = subject;
        using var __ = root;
        using var npgsqlSource = new ActivitySource(NpgsqlActivityTagNames.SourceName);

        using (var npgsqlActivity = npgsqlSource.StartActivity("postgres"))
        {
            npgsqlActivity!.SetTag(NpgsqlActivityTagNames.Statement, "SELECT * FROM \"Missing\"");
            npgsqlActivity.SetTag("otel.status_code", "ERROR");
            npgsqlActivity.SetTag("otel.status_description", "42P01");
            npgsqlActivity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                ["exception.type"] = "Npgsql.PostgresException",
                ["exception.message"] = "42P01: relation \"Missing\" does not exist",
                ["exception.stacktrace"] = "at Npgsql.Internal.NpgsqlConnector.ReadMessageLong(...)",
            }));
        }

        var span = recorder.GetActiveTrace(traceId)!.Spans.Single();
        span.Status.ShouldBe(FlowStatus.Error);
        span.IsErrorOrigin.ShouldBeTrue();
        span.ErrorType.ShouldBe("Npgsql.PostgresException");
        span.ErrorMessage.ShouldBe("42P01: relation \"Missing\" does not exist");
    }

    [Fact]
    public void ActivityStarted_WithDefaultExternalSources_DoesNotObserveNpgsqlActivity()
    {
        var (recorder, subject, traceId, root) = Arrange();
        using var _ = subject;
        using var __ = root;
        using var npgsqlSource = new ActivitySource(NpgsqlActivityTagNames.SourceName);

        using (npgsqlSource.StartActivity("postgres"))
        {
        }

        recorder.GetActiveTrace(traceId)!.Spans.ShouldBeEmpty();
    }
}
