using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.EntityFrameworkCore;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using Npgsql;
using Shouldly;
using Xunit.Abstractions;

namespace NetApiFlow.IntegrationTests;

[Collection(NpgsqlTestGroup.Name)]
[Trait("RequiresDocker", "true")]
public sealed class NpgsqlInteropIntegrationTests(NpgsqlContainerFixture fixture, ITestOutputHelper output)
{
    private const string UserByEmailSql = "SELECT \"Id\" FROM \"Users\" WHERE \"Email\" = $1";
    private const int ConcurrencyRounds = 10;

    [Fact]
    public async Task EfCoreQuery_WithTheInterceptorActive_RecordsOnlyTheInterceptorSpan()
    {
        var options = new NetApiFlowOptions { ExternalSources = ExternalActivitySource.Npgsql };
        var (recorder, listener, traceId, root) = Arrange(options);
        using var _ = listener;
        using var __ = root;

        await QueryUserWithEntityFrameworkCoreAsync(options, 0);

        var databaseSpans = DatabaseSpansOf(recorder, traceId);
        databaseSpans.Count.ShouldBe(1);
        databaseSpans[0].Component.ShouldBe("EntityFrameworkCore");
    }

    [Fact]
    public async Task RawNpgsqlCommand_WithoutEntityFrameworkCore_RecordsExactlyOneDatabaseSpan()
    {
        var (recorder, listener, traceId, root) = Arrange(new NetApiFlowOptions { ExternalSources = ExternalActivitySource.Npgsql });
        using var _ = listener;
        using var __ = root;

        await QueryUserWithRawNpgsqlAsync(0);

        var span = DatabaseSpansOf(recorder, traceId).Single();
        span.Component.ShouldBe("Npgsql");
        span.Operation.ShouldBe("SELECT Users");
        span.Status.ShouldBe(FlowStatus.Success);
        span.Tags.Keys.ShouldNotContain("sql");
    }

    [Fact]
    public async Task RawNpgsqlCommand_WithDefaultExternalSources_RecordsNoSpan()
    {
        var (recorder, listener, traceId, root) = Arrange(new NetApiFlowOptions());
        using var _ = listener;
        using var __ = root;

        await QueryUserWithRawNpgsqlAsync(0);

        recorder.GetActiveTrace(traceId)!.Spans.ShouldBeEmpty();
    }

    [Fact]
    public async Task RawNpgsqlCommand_WithCaptureSqlText_RecordsTheCommandTextWithoutTheParameterValue()
    {
        var (recorder, listener, traceId, root) = Arrange(new NetApiFlowOptions
        {
            ExternalSources = ExternalActivitySource.Npgsql,
            CaptureSqlText = true,
        });
        using var _ = listener;
        using var __ = root;

        await QueryUserWithRawNpgsqlAsync(0);

        var span = DatabaseSpansOf(recorder, traceId).Single();
        span.Tags["sql"].ShouldBe(UserByEmailSql);
        span.Tags.Keys.ShouldBe(["sql"]);
        span.Tags.Values.ShouldNotContain(value => value.Contains(NpgsqlContainerFixture.UserEmail(0), StringComparison.Ordinal));
    }

    [Fact]
    public async Task RawNpgsqlCommand_WhenTheCommandFails_MarksTheSpanAsTheErrorOrigin()
    {
        var (recorder, listener, traceId, root) = Arrange(new NetApiFlowOptions { ExternalSources = ExternalActivitySource.Npgsql });
        using var _ = listener;
        using var __ = root;

        using (var command = fixture.DataSource.CreateCommand("SELECT \"Id\" FROM \"TableThatDoesNotExist\""))
        {
            await Should.ThrowAsync<PostgresException>(() => command.ExecuteScalarAsync());
        }

        var span = DatabaseSpansOf(recorder, traceId).Single();
        output.WriteLine($"status={span.Status} isErrorOrigin={span.IsErrorOrigin} " +
            $"errorType={span.ErrorType ?? "<null>"} errorMessage={span.ErrorMessage ?? "<null>"}");

        span.Status.ShouldBe(FlowStatus.Error);
        span.IsErrorOrigin.ShouldBeTrue();
        span.ErrorType.ShouldBe("Npgsql.PostgresException");
        span.ErrorMessage.ShouldNotBeNull();
        span.ErrorMessage.ShouldContain("TableThatDoesNotExist");
    }

    [Fact]
    public async Task ConcurrentEfCoreQueries_WithTheInterceptorActive_NeverRecordADuplicateSpan()
    {
        var options = new NetApiFlowOptions { ExternalSources = ExternalActivitySource.Npgsql };
        var wrappedOptions = Options.Create(options);
        var recorder = new FlowRecorder(wrappedOptions);
        using var listener = new FlowActivityListener(recorder, wrappedOptions, NullLoggerFactory.Instance);

        for (var round = 0; round < ConcurrencyRounds; round++)
        {
            var traceIds = await Task.WhenAll(Enumerable
                .Range(0, NpgsqlContainerFixture.ConcurrentQueryCount)
                .Select(index => RunQueryUnderItsOwnTraceAsync(recorder, options, index)));

            foreach (var traceId in traceIds)
            {
                var databaseSpans = DatabaseSpansOf(recorder, traceId);
                databaseSpans.Count.ShouldBe(1, $"round {round}, trace {traceId}");
                databaseSpans[0].Component.ShouldBe("EntityFrameworkCore");
            }
        }

        output.WriteLine($"rounds={ConcurrencyRounds} queriesPerRound={NpgsqlContainerFixture.ConcurrentQueryCount} " +
            $"duplicateSpans=0");
    }

    private static (FlowRecorder Recorder, FlowActivityListener Listener, string TraceId, Activity Root) Arrange(
        NetApiFlowOptions options)
    {
        var wrappedOptions = Options.Create(options);
        var recorder = new FlowRecorder(wrappedOptions);
        var listener = new FlowActivityListener(recorder, wrappedOptions, NullLoggerFactory.Instance);

        var root = new Activity("NpgsqlInteropRoot");
        root.Start();
        var traceId = root.TraceId.ToString();
        recorder.TryStartTrace(traceId, "GET", "/api/test", DateTime.UtcNow);

        return (recorder, listener, traceId, root);
    }

    private static List<FlowSpan> DatabaseSpansOf(FlowRecorder recorder, string traceId) =>
        [.. recorder.GetActiveTrace(traceId)!.Spans.Where(span => span.Type == ComponentType.Database)];

    private async Task<string> RunQueryUnderItsOwnTraceAsync(FlowRecorder recorder, NetApiFlowOptions options, int index)
    {
        await Task.Yield();

        using var root = new Activity($"NpgsqlConcurrentRoot{index}");
        root.Start();
        var traceId = root.TraceId.ToString();
        recorder.TryStartTrace(traceId, "GET", "/api/test", DateTime.UtcNow);

        await QueryUserWithEntityFrameworkCoreAsync(options, index);

        return traceId;
    }

    private async Task QueryUserWithEntityFrameworkCoreAsync(NetApiFlowOptions options, int index)
    {
        var email = NpgsqlContainerFixture.UserEmail(index);
        await using var context = NpgsqlTestDbContext.Create(
            fixture.DataSource, new FlowDbCommandInterceptor(Options.Create(options)));

        var found = await context.Users.Where(user => user.Email == email).ToListAsync();
        found.Count.ShouldBe(1);
    }

    private async Task QueryUserWithRawNpgsqlAsync(int index)
    {
        using var command = fixture.DataSource.CreateCommand(UserByEmailSql);
        command.Parameters.Add(new NpgsqlParameter { Value = NpgsqlContainerFixture.UserEmail(index) });
        await command.ExecuteScalarAsync();
    }
}
