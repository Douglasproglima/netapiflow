using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using NetApiFlow.TestSupport;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Instrumentation;

public class FlowActivityListenerDisposeTests
{
    [Fact]
    public void Dispose_CalledTwiceOnAnEnabledListener_DoesNotSilenceAnotherEnabledListener()
    {
        using var counters = new IsolatedListenerCounters();
        var survivor = CreateListener(enabled: true);
        var disposedTwice = CreateListener(enabled: true);
        var disabled = CreateListener(enabled: false);
        using var survivingListener = survivor.Listener;
        using var disabledListener = disabled.Listener;
        var survivingRecorder = survivor.Recorder;

        using var root = new Activity("DisposeRoot");
        root.Start();
        var traceId = FlowTestRequest.Begin(survivingRecorder, root);

        using (Flow.Step("StockService", "ReserveAsync", ComponentType.Service))
        {
        }

        survivingRecorder.GetActiveTrace(traceId)!.Spans.Count.ShouldBe(1, "the step has to be recorded before any dispose, or this test proves nothing");

        disposedTwice.Listener.Dispose();
        disposedTwice.Listener.Dispose();

        using (Flow.Step("StockService", "ReleaseAsync", ComponentType.Service))
        {
        }

        survivingRecorder.GetActiveTrace(traceId)!.Spans.Count.ShouldBe(2);
    }

    private static (FlowRecorder Recorder, FlowActivityListener Listener) CreateListener(bool enabled)
    {
        var options = Options.Create(new NetApiFlowOptions { Enabled = enabled });
        var recorder = new FlowRecorder(options);
        return (recorder, new FlowActivityListener(recorder, options, NullLoggerFactory.Instance));
    }

    private sealed class IsolatedListenerCounters : IDisposable
    {
        private static readonly FieldInfo RegisteredListeners = CounterField("_registeredListeners");
        private static readonly FieldInfo EnabledListeners = CounterField("_enabledListeners");

        private readonly object? _registeredBefore = RegisteredListeners.GetValue(null);
        private readonly object? _enabledBefore = EnabledListeners.GetValue(null);

        public IsolatedListenerCounters()
        {
            RegisteredListeners.SetValue(null, 0);
            EnabledListeners.SetValue(null, 0);
        }

        public void Dispose()
        {
            RegisteredListeners.SetValue(null, _registeredBefore);
            EnabledListeners.SetValue(null, _enabledBefore);
        }

        private static FieldInfo CounterField(string name) =>
            typeof(FlowActivitySource).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"FlowActivitySource has no static field named {name}.");
    }
}
