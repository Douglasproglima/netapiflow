using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Diagnostics;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using NetApiFlow.TestSupport;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Instrumentation;

public class FlowAuthorizationFilterListenerPendingStepTests
{
    private const string BeforeOnAuthorization = "Microsoft.AspNetCore.Mvc.BeforeOnAuthorization";
    private const string AfterOnAuthorization = "Microsoft.AspNetCore.Mvc.AfterOnAuthorization";

    private sealed class StubFilter : IFilterMetadata;

    private static KeyValuePair<string, object?> BeforeEvent(IFilterMetadata filter) =>
        new(BeforeOnAuthorization, new BeforeAuthorizationFilterOnAuthorizationEventData(new ActionDescriptor(), AuthorizationContext(filter), filter));

    private static KeyValuePair<string, object?> AfterEvent(IFilterMetadata filter) =>
        new(AfterOnAuthorization, new AfterAuthorizationFilterOnAuthorizationEventData(new ActionDescriptor(), AuthorizationContext(filter), filter));

    private static AuthorizationFilterContext AuthorizationContext(IFilterMetadata filter) =>
        new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [filter]);

    [Fact]
    public void OnNext_WhenTheStepOfAFilterIsNotTracked_KeepsNothingPendingForAnAfterEventThatNeverComes()
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        var activityListener = new FlowActivityListener(recorder, options, NullLoggerFactory.Instance);
        using var root = new Activity("PendingStepRoot");
        root.Start();
        var traceId = FlowTestRequest.Begin(recorder, root);
        using var subject = new FlowAuthorizationFilterListener(NullLogger<FlowAuthorizationFilterListener>.Instance);

        var trackedFilter = new StubFilter();
        subject.OnNext(BeforeEvent(trackedFilter));
        subject.PendingStepCount.ShouldBe(1, "a tracked step waits for its after event, or this test proves nothing");
        subject.OnNext(AfterEvent(trackedFilter));
        subject.PendingStepCount.ShouldBe(0);

        activityListener.Dispose();
        Flow.CurrentTraceId.ShouldBe(traceId, "the request is still tracked");
        using (var probe = Flow.Step("Probe", "Untracked"))
        {
            probe.IsNoOp.ShouldBeTrue("with nothing listening to the source, no step is opened");
        }

        subject.OnNext(BeforeEvent(new StubFilter()));

        subject.PendingStepCount.ShouldBe(0);
    }
}
