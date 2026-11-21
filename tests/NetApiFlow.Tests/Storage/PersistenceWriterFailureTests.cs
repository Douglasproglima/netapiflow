using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using NetApiFlow.Storage;
using NetApiFlow.TestSupport;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Storage;

[Collection("FlowMetrics")]
public class PersistenceWriterFailureTests
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ExecuteAsync_WhenTheStoreKeepsFailing_TriesThreeTimesThenDiscardsAndCountsTheRest()
    {
        await using var harness = await Harness.StartAsync();

        harness.Complete("t1", "t2", "t3", "t4", "t5", "t6", "t7");
        await harness.WaitForFailuresAsync(7);

        harness.SaveCalls.ShouldBe(3);
        harness.Writer.FailureCount.ShouldBe(7);
        harness.PersistenceFailureMeasurements.ShouldBe(7);
        harness.Logs.Count(line => line.Level == LogLevel.Warning).ShouldBe(3);
        harness.Logs.Where(line => line.Level >= LogLevel.Warning && line.HasException).ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithOneFailureAmongSuccesses_NeverOpensTheCircuit()
    {
        await using var harness = await Harness.StartAsync();
        harness.StoreFailsOnCalls(1);

        harness.Complete("t1", "t2", "t3");
        await harness.WaitForSavedAsync(2);

        harness.SaveCalls.ShouldBe(3);
        harness.Writer.FailureCount.ShouldBe(1);
        harness.Logs.Count(line => line.Level == LogLevel.Warning).ShouldBe(1);
        harness.Logs.Count(line => line.Level == LogLevel.Information).ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenASuccessInterruptsTheFailures_StartsCountingAgain()
    {
        await using var harness = await Harness.StartAsync();
        harness.StoreFailsOnCalls(1, 2, 4, 5);

        harness.Complete("t1", "t2", "t3", "t4", "t5", "t6");
        await harness.WaitForSavedAsync(2);

        harness.SaveCalls.ShouldBe(6);
        harness.Writer.FailureCount.ShouldBe(4);
        harness.Logs.Count(line => line.Level == LogLevel.Warning).ShouldBe(4);
        harness.Logs.Count(line => line.Level == LogLevel.Information).ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_AfterTheRetryInterval_TriesAgainAndLogsTheRecoveryOnce()
    {
        await using var harness = await Harness.StartAsync();
        harness.Complete("t1", "t2", "t3");
        await harness.WaitForFailuresAsync(3);
        harness.StoreRecovers();

        harness.Complete("t4");
        await harness.WaitForFailuresAsync(4);
        harness.SaveCalls.ShouldBe(3);

        harness.Clock.Advance(RetryInterval + TimeSpan.FromSeconds(1));
        harness.Complete("t5", "t6", "t7");
        await harness.WaitForSavedAsync(3);

        harness.SaveCalls.ShouldBe(6);
        harness.Writer.FailureCount.ShouldBe(4);
        harness.Logs.Count(line => line.Level == LogLevel.Warning).ShouldBe(3);
        harness.Logs.Count(line => line.Level == LogLevel.Information && line.Message.Contains("4 traces", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheRetryFailsToo_WarnsAgainOncePerInterval()
    {
        await using var harness = await Harness.StartAsync();
        harness.Complete("t1", "t2", "t3");
        await harness.WaitForFailuresAsync(3);

        harness.Clock.Advance(RetryInterval + TimeSpan.FromSeconds(1));
        harness.Complete("t4", "t5", "t6");
        await harness.WaitForFailuresAsync(6);

        harness.SaveCalls.ShouldBe(4);
        harness.Logs.Count(line => line.Level == LogLevel.Warning).ShouldBe(4);
        harness.Logs.Where(line => line.Level >= LogLevel.Warning && line.HasException).ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheStoreNeverFails_LogsNothingAndSavesEveryTrace()
    {
        await using var harness = await Harness.StartAsync();
        harness.StoreRecovers();

        harness.Complete("t1", "t2", "t3");
        await harness.WaitForSavedAsync(3);

        harness.Writer.FailureCount.ShouldBe(0);
        harness.Logs.ShouldBeEmpty();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly FlowRecorder _recorder;
        private readonly MeterListener _listener;
        private readonly ConcurrentQueue<string> _saved = new();
        private long _persistenceFailureMeasurements;
        private int _saveCalls;
        private volatile Func<int, bool> _failsOnCall = _ => true;

        private Harness(NetApiFlowOptions flowOptions)
        {
            var options = Options.Create(flowOptions);
            _recorder = new FlowRecorder(options);
            Clock = new ManualTimeProvider();
            Logger = new CapturingLogger();
            Writer = new PersistenceWriter(_recorder, new ScriptedTraceStore(Save), options, Logger, new PersistenceRetryInterval(RetryInterval), Clock);
            _listener = BuildListener();
        }

        public PersistenceWriter Writer { get; }

        public ManualTimeProvider Clock { get; }

        public CapturingLogger Logger { get; }

        public IReadOnlyCollection<LogLine> Logs => Logger.Lines.ToArray();

        public int SaveCalls => Volatile.Read(ref _saveCalls);

        public long PersistenceFailureMeasurements => Interlocked.Read(ref _persistenceFailureMeasurements);

        public static async Task<Harness> StartAsync()
        {
            var harness = new Harness(new NetApiFlowOptions());
            await harness.Writer.StartAsync(CancellationToken.None);
            return harness;
        }

        public void StoreRecovers() => _failsOnCall = _ => false;

        public void StoreFailsOnCalls(params int[] calls) => _failsOnCall = call => calls.Contains(call);

        public void Complete(params string[] traceIds)
        {
            foreach (var traceId in traceIds)
            {
                _recorder.TryStartTrace(traceId, "GET", "/api/orders", DateTime.UtcNow);
                _recorder.CompleteTrace(traceId, 200, FlowStatus.Success, null);
            }
        }

        public Task WaitForFailuresAsync(long count) => WaitUntilAsync(() => Writer.FailureCount >= count);

        public Task WaitForSavedAsync(int count) => WaitUntilAsync(() => _saved.Count >= count);

        public async ValueTask DisposeAsync()
        {
            await Writer.StopAsync(CancellationToken.None);
            Writer.Dispose();
            _listener.Dispose();
        }

        private ValueTask Save(FlowTrace trace)
        {
            var call = Interlocked.Increment(ref _saveCalls);
            if (_failsOnCall(call))
            {
                return ValueTask.FromException(new InvalidOperationException("relation \"netapiflow_traces\" does not exist"));
            }

            _saved.Enqueue(trace.TraceId);
            return ValueTask.CompletedTask;
        }

        private MeterListener BuildListener()
        {
            var listener = new MeterListener
            {
                InstrumentPublished = (instrument, meterListener) =>
                {
                    if (instrument.Meter.Name == FlowMetrics.MeterName && instrument.Name == "netapiflow.persistence.failures")
                    {
                        meterListener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => Interlocked.Add(ref _persistenceFailureMeasurements, measurement));
            listener.Start();
            return listener;
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            condition().ShouldBeTrue();
        }
    }

    private sealed class ScriptedTraceStore(Func<FlowTrace, ValueTask> save) : ITraceStore
    {
        public ValueTask SaveAsync(FlowTrace trace, IReadOnlyList<FlowSpan> spans, CancellationToken cancellationToken) => save(trace);

        public ValueTask<FlowTraceDetail?> GetAsync(string traceId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<PagedResult<FlowTraceSummary>> QueryAsync(TraceQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ComponentStat>> GetComponentStatsAsync(StatsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<TraceStats> GetTraceStatsAsync(StatsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RouteStat>> GetRouteStatsAsync(StatsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<ArchitectureMap> GetArchitectureMapAsync(string method, string routeTemplate, StatsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<FlowTraceDetail?> GetLastSuccessfulAsync(string method, string routeTemplate, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<int> ApplyRetentionAsync(RetentionPolicy policy, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed record LogLine(LogLevel Level, string Message, bool HasException);

    private sealed class CapturingLogger : ILogger<PersistenceWriter>
    {
        public ConcurrentQueue<LogLine> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Enqueue(new LogLine(logLevel, formatter(state, exception), exception is not null));
    }
}
