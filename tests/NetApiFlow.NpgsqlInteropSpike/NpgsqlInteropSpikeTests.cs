using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetApiFlow.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit.Abstractions;

namespace NetApiFlow.NpgsqlInteropSpike;

[Collection(PostgresSpikeGroup.Name)]
public sealed class NpgsqlInteropSpikeTests(PostgresSpikeFixture fixture, ITestOutputHelper output)
{
    private const string NpgsqlSourceName = "Npgsql";
    private const string NetApiFlowSourceName = "NetApiFlow";
    private const string DbStatementTagName = "db.statement";
    private const string UserByEmailSql = "SELECT \"Id\" FROM \"Users\" WHERE \"Email\" = $1";
    private const int ConcurrencyRounds = 30;

    [Fact]
    public async Task Spike_WithoutConfigureTracing_ReportsWhetherNpgsqlEmitsAnything()
    {
        using var recorder = new ActivitySnapshotRecorder();
        await using var dataSource = fixture.CreateUntracedDataSource();
        var probeEmail = PostgresSpikeFixture.LeakProbeEmail;

        await using (var context = SpikeDbContext.Create(dataSource, NewInterceptor()))
        {
            using var root = StartRoot("spike-untraced");
            await context.Users.Where(user => user.Email == probeEmail).ToListAsync();
        }

        ReportSources(recorder);
        ReportSnapshots(recorder, "every activity recorded without ConfigureTracing");

        var npgsql = recorder.From(NpgsqlSourceName);
        foreach (var snapshot in npgsql)
        {
            output.WriteLine(snapshot.DescribeTags());
        }

        output.WriteLine($"ANSWER premise(1): Npgsql activities without ConfigureTracing = {npgsql.Count}");
    }

    [Fact]
    public async Task Spike_SequentialEfCoreQuery_ReportsNpgsqlActivityParent()
    {
        using var recorder = new ActivitySnapshotRecorder();
        await using var dataSource = fixture.CreateTracedDataSource();

        await using (var context = SpikeDbContext.Create(dataSource, NewInterceptor()))
        {
            using var root = StartRoot("spike-sequential");
            var found = await context.Users.Where(user => user.Email == PostgresSpikeFixture.LeakProbeEmail).ToListAsync();
            found.Count.ShouldBe(1);
        }

        ReportSources(recorder);
        ReportSnapshots(recorder, "every activity recorded for one sequential EF Core query");

        var npgsql = recorder.From(NpgsqlSourceName);
        output.WriteLine($"ANSWER question(1) sequential: Npgsql activities = {npgsql.Count}, " +
            $"parented by {NetApiFlowSourceName} = {npgsql.Count(IsParentedByNetApiFlow)}");
    }

    [Fact]
    public async Task Spike_ConcurrentEfCoreQueries_ReportsParentChainStabilityAcrossRounds()
    {
        await using var dataSource = fixture.CreateTracedDataSource();
        await MeasureConcurrentParentChainAsync(dataSource, "question(1) concurrent, pooled");
    }

    [Fact]
    public async Task Spike_ConcurrentEfCoreQueriesWithMultiplexing_ReportsParentChainStabilityAcrossRounds()
    {
        await using var dataSource = fixture.CreateMultiplexingDataSource();
        await MeasureConcurrentParentChainAsync(dataSource, "question(1) concurrent, multiplexing");
    }

    private async Task MeasureConcurrentParentChainAsync(NpgsqlDataSource dataSource, string label)
    {
        var totalNpgsql = 0;
        var parentedByNetApiFlow = 0;
        var parentedByOther = 0;
        var orphaned = 0;
        var wrongTrace = 0;
        var roundsWithAnyMismatch = 0;
        var observedParentSources = new HashSet<string>(StringComparer.Ordinal);

        for (var round = 0; round < ConcurrencyRounds; round++)
        {
            using var recorder = new ActivitySnapshotRecorder();

            var traceIds = await Task.WhenAll(Enumerable
                .Range(0, PostgresSpikeFixture.ConcurrentQueryCount)
                .Select(index => RunIsolatedQueryAsync(dataSource, index)));

            var expectedTraceIds = new HashSet<string>(traceIds, StringComparer.Ordinal);
            var npgsql = recorder.From(NpgsqlSourceName);
            var mismatchesThisRound = 0;

            foreach (var snapshot in npgsql)
            {
                totalNpgsql++;
                observedParentSources.Add(snapshot.ParentSourceName ?? "<null>");

                if (IsParentedByNetApiFlow(snapshot))
                {
                    parentedByNetApiFlow++;
                }
                else if (snapshot.ParentId is null)
                {
                    orphaned++;
                    mismatchesThisRound++;
                }
                else
                {
                    parentedByOther++;
                    mismatchesThisRound++;
                }

                if (!expectedTraceIds.Contains(snapshot.TraceId))
                {
                    wrongTrace++;
                    mismatchesThisRound++;
                }
            }

            if (npgsql.Count != PostgresSpikeFixture.ConcurrentQueryCount || mismatchesThisRound > 0)
            {
                roundsWithAnyMismatch++;
                output.WriteLine($"round {round}: npgsqlActivities={npgsql.Count} mismatches={mismatchesThisRound}");
                foreach (var snapshot in npgsql)
                {
                    output.WriteLine($"  {snapshot.Describe()}");
                }
            }
        }

        output.WriteLine($"ANSWER {label}: rounds={ConcurrencyRounds} " +
            $"queriesPerRound={PostgresSpikeFixture.ConcurrentQueryCount} npgsqlActivities={totalNpgsql}");
        output.WriteLine($"  parentedBy{NetApiFlowSourceName}={parentedByNetApiFlow} parentedByOtherSource={parentedByOther} " +
            $"orphaned={orphaned} wrongTraceId={wrongTrace} roundsWithAnyMismatch={roundsWithAnyMismatch}");
        output.WriteLine($"  observed parent sources: {string.Join(", ", observedParentSources.Order(StringComparer.Ordinal))}");

        totalNpgsql.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Spike_ParameterizedQuery_ReportsRawDbStatementTag()
    {
        using var recorder = new ActivitySnapshotRecorder();
        await using var dataSource = fixture.CreateTracedDataSource();
        var probeEmail = PostgresSpikeFixture.LeakProbeEmail;

        await using (var context = SpikeDbContext.Create(dataSource, NewInterceptor()))
        {
            using var root = StartRoot("spike-db-statement");
            await context.Users.Where(user => user.Email == probeEmail).ToListAsync();
        }

        ReportTags(recorder.From(NpgsqlSourceName), "question(2) EF Core, parameterized");
    }

    [Fact]
    public async Task Spike_EfCoreFilterOnStaticReadonlyField_ReportsLiteralInlinedIntoDbStatement()
    {
        using var recorder = new ActivitySnapshotRecorder();
        await using var dataSource = fixture.CreateTracedDataSource();

        await using (var context = SpikeDbContext.Create(dataSource, NewInterceptor()))
        {
            using var root = StartRoot("spike-inlined-literal");
            await context.Users.Where(user => user.Email == PostgresSpikeFixture.LeakProbeEmail).ToListAsync();
        }

        var npgsql = recorder.From(NpgsqlSourceName);
        npgsql.ShouldNotBeEmpty();

        var statements = npgsql.Select(snapshot => snapshot.Tag(DbStatementTagName)).ToList();
        foreach (var statement in statements)
        {
            output.WriteLine($"db.statement = {statement}");
        }

        var inlined = statements.Count(statement =>
            statement?.Contains(PostgresSpikeFixture.LeakProbeEmail, StringComparison.Ordinal) == true);

        output.WriteLine($"ANSWER question(2) EF Core, value not parameterized by EF Core: activities={npgsql.Count} " +
            $"statementsUsingPlaceholder={statements.Count(IsParameterPlaceholder)} statementsCarryingTheLiteral={inlined}");

        inlined.ShouldBe(npgsql.Count);
    }

    [Fact]
    public async Task Spike_RawNpgsqlParameterizedCommand_ReportsRawDbStatementTag()
    {
        using var recorder = new ActivitySnapshotRecorder();
        await using var dataSource = fixture.CreateTracedDataSource();

        using (var root = StartRoot("spike-db-statement-raw"))
        {
            using var command = dataSource.CreateCommand(UserByEmailSql);
            command.Parameters.Add(new NpgsqlParameter { Value = PostgresSpikeFixture.LeakProbeEmail });
            await command.ExecuteScalarAsync();
        }

        ReportTags(recorder.From(NpgsqlSourceName), "question(2) raw Npgsql, parameterized");
    }

    private void ReportTags(IReadOnlyList<ActivitySnapshot> npgsql, string label)
    {
        npgsql.ShouldNotBeEmpty();

        foreach (var snapshot in npgsql)
        {
            output.WriteLine(snapshot.Describe());
            output.WriteLine(snapshot.DescribeTags());
        }

        var statements = npgsql.Select(snapshot => snapshot.Tag(DbStatementTagName)).ToList();
        var parameterized = statements.Count(IsParameterPlaceholder);

        var leaking = npgsql
            .SelectMany(snapshot => snapshot.Tags)
            .Where(tag => tag.Value?.Contains(PostgresSpikeFixture.LeakProbeEmail, StringComparison.Ordinal) == true)
            .ToList();

        output.WriteLine($"ANSWER {label}: activities={npgsql.Count} statementsUsingPlaceholder={parameterized} " +
            $"tagsCarryingRawParameterValue={leaking.Count}");
        foreach (var tag in leaking)
        {
            output.WriteLine($"  LEAK {tag.Key} = {tag.Value}");
        }

        parameterized.ShouldBe(npgsql.Count);
        leaking.ShouldBeEmpty();
    }

    [Fact]
    public async Task Spike_RawNpgsqlWithoutEntityFrameworkCore_ReportsParentOfNativeActivity()
    {
        using var recorder = new ActivitySnapshotRecorder();
        await using var dataSource = fixture.CreateTracedDataSource();

        using (var root = StartRoot("spike-raw-npgsql"))
        {
            using var command = dataSource.CreateCommand(UserByEmailSql);
            command.Parameters.Add(new NpgsqlParameter { Value = PostgresSpikeFixture.LeakProbeEmail });
            await command.ExecuteScalarAsync();
        }

        ReportSources(recorder);
        ReportSnapshots(recorder, "every activity recorded for raw Npgsql without EF Core");

        var npgsql = recorder.From(NpgsqlSourceName);
        npgsql.ShouldNotBeEmpty();
        foreach (var snapshot in npgsql)
        {
            output.WriteLine(snapshot.DescribeTags());
        }

        output.WriteLine($"ANSWER question(3): Npgsql activities = {npgsql.Count}, " +
            $"parented by {NetApiFlowSourceName} = {npgsql.Count(IsParentedByNetApiFlow)}");

        npgsql.ShouldAllBe(snapshot => snapshot.ParentSourceName != NetApiFlowSourceName);
    }

    private static async Task<string> RunIsolatedQueryAsync(NpgsqlDataSource dataSource, int index)
    {
        await Task.Yield();

        using var root = StartRoot($"spike-concurrent-{index}");
        await using var context = SpikeDbContext.Create(dataSource, NewInterceptor());
        await context.Users.Where(user => user.Email == PostgresSpikeFixture.ConcurrentEmail(index)).ToListAsync();

        return root.TraceId.ToString();
    }

    private static bool IsParameterPlaceholder(string? statement) =>
        statement?.Contains("$1", StringComparison.Ordinal) == true
        || statement?.Contains("@__", StringComparison.Ordinal) == true;

    private static FlowDbCommandInterceptor NewInterceptor() => new(Options.Create(new NetApiFlowOptions()));

    private static Activity StartRoot(string name)
    {
        var root = new Activity(name);
        root.Start();
        return root;
    }

    private static bool IsParentedByNetApiFlow(ActivitySnapshot snapshot) =>
        snapshot.ParentSourceName == NetApiFlowSourceName;

    private void ReportSources(ActivitySnapshotRecorder recorder) =>
        output.WriteLine($"activity sources observed: {string.Join(", ", recorder.SourceNames)}");

    private void ReportSnapshots(ActivitySnapshotRecorder recorder, string title)
    {
        output.WriteLine(title);
        foreach (var snapshot in recorder.All)
        {
            output.WriteLine($"  {snapshot.Describe()}");
        }
    }
}
