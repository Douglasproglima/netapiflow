using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using NetApiFlow.Storage;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class BackgroundServiceFailureIntegrationTests
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShutdownLimit = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RetentionTick_WhenTheStoreThrowsOnEveryTick_TheHostKeepsRunningAndRetentionIsTriedAgain()
    {
        var store = new FaultyTraceStore(onRetention: (_, _) => throw new InvalidOperationException("the database is down"));
        using var host = await StartHostAsync(store);
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        var triedAgain = await WaitUntilAsync(() => store.RetentionCalls >= 3);

        triedAgain.ShouldBeTrue($"retention was tried {store.RetentionCalls} time(s) before the service stopped");
        lifetime.ApplicationStopping.IsCancellationRequested.ShouldBeFalse();
        (await host.GetTestServer().CreateClient().GetAsync("/ping")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RetentionTick_WhenTheStoreFailsThenRecovers_RetentionRunsAtALaterTick()
    {
        var store = new FaultyTraceStore(onRetention: (call, _) =>
            call <= 2 ? throw new InvalidOperationException("the database is down") : Task.CompletedTask);
        using var host = await StartHostAsync(store);

        var recovered = await WaitUntilAsync(() => store.RetentionSuccesses >= 1);

        recovered.ShouldBeTrue($"retention succeeded {store.RetentionSuccesses} time(s) after {store.RetentionCalls} call(s)");
    }

    [Fact]
    public async Task RetentionTick_WhenTheStoreThrows_LogsAWarningWithTheExceptionAndTheRequestIsNotAffected()
    {
        var logs = new CapturingLoggerProvider();
        var store = new FaultyTraceStore(onRetention: (_, _) => throw new InvalidOperationException("the database is down"));
        using var host = await StartHostAsync(store, logs);

        var logged = await WaitUntilAsync(() => logs.Entries.Any(entry => entry.Level == LogLevel.Warning && entry.Exception is not null));
        var response = await host.GetTestServer().CreateClient().GetAsync("/ping");

        logged.ShouldBeTrue("no warning with the exception was logged");
        var warning = logs.Entries.First(entry => entry.Level == LogLevel.Warning && entry.Exception is not null);
        warning.Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("the database is down");
        warning.Message.ShouldContain("retention will be tried again");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StopAsync_WithRetentionFailingOnEveryTick_FinishesQuicklyWithoutThrowing()
    {
        var store = new FaultyTraceStore(onRetention: (_, _) => throw new InvalidOperationException("the database is down"));
        using var host = await StartHostAsync(store);
        await WaitUntilAsync(() => store.RetentionCalls >= 1);

        var stopwatch = Stopwatch.StartNew();
        await host.StopAsync();
        stopwatch.Stop();

        stopwatch.Elapsed.ShouldBeLessThan(ShutdownLimit);
    }

    [Fact]
    public async Task StopAsync_WithARetentionCallInFlight_FinishesQuicklyWithoutThrowing()
    {
        var store = new FaultyTraceStore(onRetention: async (_, token) => await Task.Delay(Timeout.InfiniteTimeSpan, token));
        using var host = await StartHostAsync(store);
        await WaitUntilAsync(() => store.RetentionCalls >= 1);

        var stopwatch = Stopwatch.StartNew();
        await host.StopAsync();
        stopwatch.Stop();

        stopwatch.Elapsed.ShouldBeLessThan(ShutdownLimit);
    }

    public static TheoryData<Func<Exception>> CancellationsThatAreNotAShutdown() => new()
    {
        () => new OperationCanceledException("the command timed out"),
        () => new TaskCanceledException("the command timed out"),
    };

    [Theory]
    [MemberData(nameof(CancellationsThatAreNotAShutdown))]
    public async Task SaveAsync_WhenTheStoreThrowsACancellationOutsideShutdown_CountsAFailureAndSavesTheNextTrace(
        Func<Exception> createCancellation)
    {
        var store = new FaultyTraceStore(onSave: (call, _) => call == 1 ? throw createCancellation() : Task.CompletedTask);
        using var host = await StartHostAsync(store);
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var writer = host.Services.GetRequiredService<PersistenceWriter>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        CompleteTrace(recorder, "first-trace");
        await WaitUntilAsync(() => store.SaveCalls >= 1);
        CompleteTrace(recorder, "next-trace");
        var savedTheNextOne = await WaitUntilAsync(() => store.SavedTraceIds.Contains("next-trace"));

        savedTheNextOne.ShouldBeTrue($"the writer saved [{string.Join(", ", store.SavedTraceIds)}] and the host stopping is {lifetime.ApplicationStopping.IsCancellationRequested}");
        writer.FailureCount.ShouldBe(1);
        lifetime.ApplicationStopping.IsCancellationRequested.ShouldBeFalse();
    }

    private static void CompleteTrace(FlowRecorder recorder, string traceId)
    {
        recorder.TryStartTrace(traceId, "GET", "/api/orders", DateTime.UtcNow);
        recorder.CompleteTrace(traceId, 200, FlowStatus.Success, null);
    }

    private static async Task<IHost> StartHostAsync(FaultyTraceStore store, ILoggerProvider? loggerProvider = null)
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                if (loggerProvider is not null)
                {
                    logging.AddProvider(loggerProvider);
                }
            })
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<ITraceStore>(store);
                    services.AddNetApiFlow();
                    services.AddSingleton(new RetentionInterval(TickInterval));
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGet("/ping", () => "pong"));
                });
            })
            .StartAsync();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }

    private sealed class FaultyTraceStore(
        Func<int, CancellationToken, Task>? onRetention = null,
        Func<int, CancellationToken, Task>? onSave = null) : ITraceStore
    {
        private readonly Func<int, CancellationToken, Task> _onRetention = onRetention ?? ((_, _) => Task.CompletedTask);
        private readonly Func<int, CancellationToken, Task> _onSave = onSave ?? ((_, _) => Task.CompletedTask);
        private readonly ConcurrentQueue<string> _saved = new();
        private int _retentionCalls;
        private int _retentionSuccesses;
        private int _saveCalls;

        public int RetentionCalls => Volatile.Read(ref _retentionCalls);

        public int RetentionSuccesses => Volatile.Read(ref _retentionSuccesses);

        public int SaveCalls => Volatile.Read(ref _saveCalls);

        public IReadOnlyCollection<string> SavedTraceIds => _saved.ToArray();

        public async ValueTask<int> ApplyRetentionAsync(RetentionPolicy policy, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _retentionCalls);
            await _onRetention(call, cancellationToken);
            Interlocked.Increment(ref _retentionSuccesses);
            return 0;
        }

        public async ValueTask SaveAsync(FlowTrace trace, IReadOnlyList<FlowSpan> spans, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _saveCalls);
            await _onSave(call, cancellationToken);
            _saved.Enqueue(trace.TraceId);
        }

        public ValueTask<FlowTraceDetail?> GetAsync(string traceId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<FlowTraceDetail?>(null);

        public ValueTask<PagedResult<FlowTraceSummary>> QueryAsync(TraceQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ComponentStat>> GetComponentStatsAsync(StatsQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<TraceStats> GetTraceStatsAsync(StatsQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RouteStat>> GetRouteStatsAsync(StatsQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<ArchitectureMap> GetArchitectureMapAsync(string method, string routeKey, StatsQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<FlowTraceDetail?> GetLastSuccessfulAsync(string method, string routeKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult<FlowTraceDetail?>(null);
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }
}
