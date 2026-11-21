using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Instrumentation;
using NetApiFlow.Middleware;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Instrumentation;

public sealed class EndpointMatchedListenerTests
{
    private const string ListenerName = "Microsoft.AspNetCore";
    private const string EndpointMatchedEventName = "Microsoft.AspNetCore.Routing.EndpointMatched";

    public static string Handler() => "handled";

    [Fact]
    public void Start_AfterBeingCalledTwice_SubscribesOnce()
    {
        using var listener = new EndpointMatchedListener(NullLogger<EndpointMatchedListener>.Instance);
        using var scenario = new Scenario(listener);

        listener.Start();
        listener.Start();
        scenario.Publish();

        listener.IsSubscribed.ShouldBeTrue();
        scenario.EndpointSteps().ShouldHaveSingleItem();
    }

    [Fact]
    public void Start_AfterDispose_DoesNotSubscribe()
    {
        var listener = new EndpointMatchedListener(NullLogger<EndpointMatchedListener>.Instance);
        using var scenario = new Scenario(listener);
        listener.Dispose();

        listener.Start();
        scenario.Publish();

        listener.IsSubscribed.ShouldBeFalse();
        scenario.EndpointSteps().ShouldBeEmpty();
    }

    [Fact]
    public void Dispose_WithoutStart_DoesNotThrow()
    {
        var listener = new EndpointMatchedListener(NullLogger<EndpointMatchedListener>.Instance);

        Should.NotThrow(listener.Dispose);

        listener.IsSubscribed.ShouldBeFalse();
    }

    [Fact]
    public void Start_AsksTheRoutingForNoEventButTheEndpointMatchedOne()
    {
        using var listener = new EndpointMatchedListener(NullLogger<EndpointMatchedListener>.Instance);
        using var scenario = new Scenario(listener);

        listener.Start();

        scenario.Source.IsEnabled(EndpointMatchedEventName).ShouldBeTrue();
        scenario.Source.IsEnabled("Microsoft.AspNetCore.Hosting.BeginRequest").ShouldBeFalse();
        scenario.Source.IsEnabled("Microsoft.AspNetCore.Mvc.BeforeAction").ShouldBeFalse();
    }

    [Fact]
    public void Event_ForARequestOfAnotherHost_OpensNoStep()
    {
        using var listener = new EndpointMatchedListener(NullLogger<EndpointMatchedListener>.Instance);
        using var anotherHostsListener = new EndpointMatchedListener(NullLogger<EndpointMatchedListener>.Instance);
        using var scenario = new Scenario(anotherHostsListener);
        listener.Start();

        scenario.Publish();

        scenario.EndpointSteps().ShouldBeEmpty();
    }

    [Fact]
    public void Event_ForARequestTheMiddlewareDidNotMarkAsTraced_OpensNoStep()
    {
        using var listener = new EndpointMatchedListener(NullLogger<EndpointMatchedListener>.Instance);
        using var scenario = new Scenario(listener, markTheRequestAsTraced: false);
        listener.Start();

        scenario.Publish();

        scenario.EndpointSteps().ShouldBeEmpty();
        scenario.Context.Items.ContainsKey(FlowHttpContextItems.ImplicitEndpointStep).ShouldBeFalse();
    }

    private sealed class Scenario : IDisposable
    {
        private readonly FlowRecorder _recorder;
        private readonly FlowActivityListener _activityListener;
        private readonly Activity _root;
        private readonly string _traceId;

        public Scenario(EndpointMatchedListener owner, bool markTheRequestAsTraced = true)
        {
            var options = Options.Create(new NetApiFlowOptions());
            _recorder = new FlowRecorder(options);
            _activityListener = new FlowActivityListener(_recorder, options, NullLoggerFactory.Instance);
            _root = new Activity("TestRoot");
            _root.Start();
            _traceId = _root.TraceId.ToString();
            _recorder.TryStartTrace(_traceId, "GET", "/handler", DateTime.UtcNow);

            Source = new DiagnosticListener(ListenerName);
            Context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(owner).BuildServiceProvider() };
            if (markTheRequestAsTraced)
            {
                Context.Items[FlowHttpContextItems.TraceStarted] = true;
            }

            var metadata = new EndpointMetadataCollection(
                typeof(EndpointMatchedListenerTests).GetMethod(nameof(Handler))!,
                new HttpMethodMetadata(["GET"]));
            Context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("/handler"), 0, metadata, "handler"));
        }

        public DiagnosticListener Source { get; }

        public DefaultHttpContext Context { get; }

        public void Publish()
        {
            if (Source.IsEnabled(EndpointMatchedEventName))
            {
                Source.Write(EndpointMatchedEventName, Context);
            }
        }

        public List<FlowSpanSnapshot> EndpointSteps()
        {
            if (Context.Items.TryGetValue(FlowHttpContextItems.ImplicitEndpointStep, out var opened) && opened is FlowStep step)
            {
                step.Dispose();
            }

            return _recorder.GetActiveTrace(_traceId)!.Spans
                .Where(span => span.Type == ComponentType.Endpoint)
                .Select(span => new FlowSpanSnapshot(span.Component, span.Operation))
                .ToList();
        }

        public void Dispose()
        {
            Source.Dispose();
            _activityListener.Dispose();
            _root.Dispose();
        }
    }

    private sealed record FlowSpanSnapshot(string Component, string Operation);
}
