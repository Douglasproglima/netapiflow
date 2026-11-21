using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using NetApiFlow.TestSupport;
using Shouldly;

namespace NetApiFlow.Storage.Tests;

public abstract class PersistenceWithoutSchemaTestsBase
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    protected abstract void AddStorage(IServiceCollection services, string tablePrefix);

    protected abstract Task CreateSchemaAsync(string tablePrefix);

    protected abstract Task<long> CountTablesAsync(string tablePrefix);

    [Fact]
    public async Task Writer_WithoutAutoCreateSchemaOnAnEmptyDatabase_WarnsThreeTimesThenOncePerIntervalAndSavesAgainOnceTheScriptRan()
    {
        var tablePrefix = $"w{Guid.NewGuid():N}_";
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        AddStorage(services, tablePrefix);
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<ITraceStore>();
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        var clock = new ManualTimeProvider();
        using var writer = new PersistenceWriter(
            recorder, store, options, provider.GetRequiredService<ILogger<PersistenceWriter>>(), new PersistenceRetryInterval(RetryInterval), clock);
        await writer.StartAsync(CancellationToken.None);
        try
        {
            Complete(recorder, "n1", "n2", "n3", "n4", "n5", "n6");
            await WaitUntilAsync(() => Task.FromResult(writer.FailureCount == 6));

            (await CountTablesAsync(tablePrefix)).ShouldBe(0);
            logs.Lines.Count(line => line.Category == typeof(PersistenceWriter).FullName && line.Level == LogLevel.Warning).ShouldBe(3);
            logs.Lines.Count(line => line.Level == LogLevel.Error && line.Message.Contains("schema.sql", StringComparison.Ordinal)).ShouldBe(1);
            logs.Lines.Where(line => line.Level >= LogLevel.Warning && line.HasException).ShouldBeEmpty();

            await CreateSchemaAsync(tablePrefix);
            clock.Advance(RetryInterval + TimeSpan.FromSeconds(1));
            Complete(recorder, "after-script");
            await WaitUntilAsync(async () => await store.GetAsync("after-script", CancellationToken.None) is not null);
        }
        finally
        {
            await writer.StopAsync(CancellationToken.None);
        }

        writer.FailureCount.ShouldBe(6);
        logs.Lines.Count(line => line.Level == LogLevel.Information && line.Message.Contains("6 traces", StringComparison.Ordinal)).ShouldBe(1);
    }

    private static void Complete(FlowRecorder recorder, params string[] traceIds)
    {
        foreach (var traceId in traceIds)
        {
            recorder.TryStartTrace(traceId, "GET", "/api/orders", DateTime.UtcNow);
            recorder.CompleteTrace(traceId, 200, FlowStatus.Success, null);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!await condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        (await condition()).ShouldBeTrue();
    }
}
