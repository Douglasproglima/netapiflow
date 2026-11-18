using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Diagnostics;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Instrumentation;

/// <summary>
/// Disposing a <see cref="DiagnosticListener"/> subscription does not synchronously guarantee the
/// observer stops receiving events, so every listener here has to refuse to act once disposed.
/// Each test delivers the event straight to the observer after <c>Dispose()</c> rather than
/// waiting for a racy hand-off to happen on its own.
/// </summary>
public class DisposedListenerTests
{
    private const string BeforeOnAuthorization = "Microsoft.AspNetCore.Mvc.BeforeOnAuthorization";
    private const string AfterOnAuthorization = "Microsoft.AspNetCore.Mvc.AfterOnAuthorization";

    private sealed class StubFilter : IFilterMetadata;

    private static BeforeAuthorizationFilterOnAuthorizationEventData BeforeEvent(IFilterMetadata filter) =>
        new(new ActionDescriptor(), AuthorizationContext(filter), filter);

    private static AfterAuthorizationFilterOnAuthorizationEventData AfterEvent(IFilterMetadata filter) =>
        new(new ActionDescriptor(), AuthorizationContext(filter), filter);

    private static AuthorizationFilterContext AuthorizationContext(IFilterMetadata filter) =>
        new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [filter]);

    private static (FlowRecorder Recorder, FlowActivityListener Listener, Activity Root, string TraceId) ArrangeTrace()
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        var listener = new FlowActivityListener(recorder, options, NullLoggerFactory.Instance);

        var root = new Activity("DisposedListenerRoot");
        root.Start();
        var traceId = root.TraceId.ToString();
        recorder.TryStartTrace(traceId, "GET", "/api/test", DateTime.UtcNow);

        return (recorder, listener, root, traceId);
    }

    [Fact]
    public void FlowAuthorizationFilterListener_AfterDispose_OpensNoFurtherStep()
    {
        var (recorder, listener, root, traceId) = ArrangeTrace();
        using var _ = listener;
        using var __ = root;

        var subject = new FlowAuthorizationFilterListener(NullLogger<FlowAuthorizationFilterListener>.Instance);
        var liveFilter = new StubFilter();
        subject.OnNext(new KeyValuePair<string, object?>(BeforeOnAuthorization, BeforeEvent(liveFilter)));
        subject.OnNext(new KeyValuePair<string, object?>(AfterOnAuthorization, AfterEvent(liveFilter)));

        var spansWhileAlive = SpansOf(recorder, traceId);
        spansWhileAlive.Count.ShouldBe(1, "the listener has to work before it is disposed, or this test proves nothing");

        subject.Dispose();

        var lateFilter = new StubFilter();
        subject.OnNext(new KeyValuePair<string, object?>(BeforeOnAuthorization, BeforeEvent(lateFilter)));
        subject.OnNext(new KeyValuePair<string, object?>(AfterOnAuthorization, AfterEvent(lateFilter)));

        SpansOf(recorder, traceId).Count.ShouldBe(1);
    }

    /// <summary>
    /// A step left pending when the listener is disposed must not stay in the map either: the
    /// trace it belongs to is finished, and nothing will ever close it.
    /// </summary>
    [Fact]
    public void FlowAuthorizationFilterListener_AfterDispose_DoesNotCloseAStepOpenedBeforeIt()
    {
        var (recorder, listener, root, traceId) = ArrangeTrace();
        using var _ = listener;
        using var __ = root;

        var subject = new FlowAuthorizationFilterListener(NullLogger<FlowAuthorizationFilterListener>.Instance);
        var filter = new StubFilter();
        subject.OnNext(new KeyValuePair<string, object?>(BeforeOnAuthorization, BeforeEvent(filter)));

        subject.Dispose();
        subject.OnNext(new KeyValuePair<string, object?>(AfterOnAuthorization, AfterEvent(filter)));

        SpansOf(recorder, traceId).ShouldAllBe(span => span.EndedAt == null || span.Status != FlowStatus.Running);
    }

    private static List<FlowSpan> SpansOf(FlowRecorder recorder, string traceId) =>
        [.. recorder.GetActiveTrace(traceId)!.Spans];

#if NET8_0
    [Fact]
    public void HttpClientActivityTagBackfill_AfterDispose_WritesNoFurtherTag()
    {
        var options = Options.Create(new NetApiFlowOptions
        {
            ExternalSources = ExternalActivitySource.HttpClient,
        });

        using var activity = new Activity("outbound");
        activity.Start();

        var subject = new HttpClientActivityTagBackfill(
            options,
            NullLogger<HttpClientActivityTagBackfill>.Instance);

        subject.OnNext(new KeyValuePair<string, object?>(
            "System.Net.Http.HttpRequestOut.Start",
            new { Request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/first") }));

        activity.GetTagItem(HttpClientActivityTagNames.UrlFull)
            .ShouldBe("https://example.test/first", "the backfill has to work before it is disposed, or this test proves nothing");

        subject.Dispose();

        subject.OnNext(new KeyValuePair<string, object?>(
            "System.Net.Http.HttpRequestOut.Start",
            new { Request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/second") }));

        activity.GetTagItem(HttpClientActivityTagNames.UrlFull).ShouldBe("https://example.test/first");
        activity.GetTagItem(HttpClientActivityTagNames.Method).ShouldBe("GET");
    }
#endif
}
