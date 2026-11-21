using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.EntityFrameworkCore;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using NetApiFlow.TestSupport;
using Shouldly;

namespace NetApiFlow.SqlClient.IntegrationTests;

/// <summary>
/// Microsoft.Data.SqlClient publishes no <see cref="ActivitySource"/>, only a
/// <see cref="DiagnosticListener"/>, so this exercises the listener bridge end to end against a
/// real SQL Server: a command issued outside EF Core produces a span, the same command through EF
/// Core produces exactly one (the interceptor's), and nothing is recorded when the flag is off.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class SqlClientInteropIntegrationTests(SqlServerContainerFixture fixture)
{
    private const string UserByEmailSql = "SELECT Id FROM Users WHERE Email = @email";

    [Fact]
    public async Task RawSqlCommand_WithTheFlagOn_RecordsExactlyOneDatabaseSpan()
    {
        using var arrangement = Arrange(new NetApiFlowOptions
        {
            ExternalSources = ExternalActivitySource.SqlClient,
        });
        var (recorder, traceId) = (arrangement.Recorder, arrangement.TraceId);

        await QueryUserWithRawCommandAsync();

        var span = DatabaseSpansOf(recorder, traceId).ShouldHaveSingleItem();
        span.Component.ShouldBe("SqlClient");
        span.Operation.ShouldBe("SELECT Users");
        span.Status.ShouldBe(FlowStatus.Success);
        span.Tags.Keys.ShouldNotContain("sql");
    }

    [Fact]
    public async Task RawSqlCommand_WithDefaultExternalSources_RecordsNoSpan()
    {
        using var arrangement = Arrange(new NetApiFlowOptions());
        var (recorder, traceId) = (arrangement.Recorder, arrangement.TraceId);

        await QueryUserWithRawCommandAsync();

        DatabaseSpansOf(recorder, traceId).ShouldBeEmpty();
    }

    [Fact]
    public async Task EfCoreQuery_WithBothTheInterceptorAndTheFlag_RecordsOnlyTheInterceptorSpan()
    {
        var options = new NetApiFlowOptions { ExternalSources = ExternalActivitySource.SqlClient };
        using var arrangement = Arrange(options);
        var (recorder, traceId) = (arrangement.Recorder, arrangement.TraceId);

        await QueryUserWithEntityFrameworkCoreAsync(options);

        var span = DatabaseSpansOf(recorder, traceId).ShouldHaveSingleItem();
        span.Component.ShouldBe("EntityFrameworkCore");
    }

    [Fact]
    public async Task RawSqlCommand_WithCaptureSqlText_RecordsTheCommandTextWithoutTheParameterValue()
    {
        using var arrangement = Arrange(new NetApiFlowOptions
        {
            ExternalSources = ExternalActivitySource.SqlClient,
            CaptureSqlText = true,
        });
        var (recorder, traceId) = (arrangement.Recorder, arrangement.TraceId);

        await QueryUserWithRawCommandAsync();

        var span = DatabaseSpansOf(recorder, traceId).ShouldHaveSingleItem();
        span.Tags["sql"].ShouldBe(UserByEmailSql);
        span.Tags.Values.ShouldNotContain(value => value.Contains(SqlServerContainerFixture.UserEmail, StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailingSqlCommand_RecordsTheSpanAsTheErrorOrigin()
    {
        using var arrangement = Arrange(new NetApiFlowOptions
        {
            ExternalSources = ExternalActivitySource.SqlClient,
        });
        var (recorder, traceId) = (arrangement.Recorder, arrangement.TraceId);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT Id FROM NoSuchTable", connection);
        await Should.ThrowAsync<SqlException>(() => command.ExecuteScalarAsync());

        var span = DatabaseSpansOf(recorder, traceId).ShouldHaveSingleItem();
        span.Status.ShouldBe(FlowStatus.Error);
        span.IsErrorOrigin.ShouldBeTrue();
        span.ErrorType.ShouldBe(typeof(SqlException).FullName);
    }

    [Fact]
    public async Task ConcurrentCommands_EachRecordItsOwnSpanUnderItsOwnTrace()
    {
        var options = new NetApiFlowOptions { ExternalSources = ExternalActivitySource.SqlClient };
        var wrappedOptions = Options.Create(options);
        var recorder = new FlowRecorder(wrappedOptions);
        using var listener = new FlowActivityListener(recorder, wrappedOptions, NullLoggerFactory.Instance);
        using var sqlClientListener = new SqlClientCommandListener(wrappedOptions, NullLogger<SqlClientCommandListener>.Instance);
        sqlClientListener.Start();

        var traceIds = await Task.WhenAll(Enumerable.Range(0, 16).Select(RunQueryUnderItsOwnTraceAsync));

        traceIds.Distinct().Count().ShouldBe(traceIds.Length);
        foreach (var traceId in traceIds)
        {
            DatabaseSpansOf(recorder, traceId).ShouldHaveSingleItem().Component.ShouldBe("SqlClient");
        }

        async Task<string> RunQueryUnderItsOwnTraceAsync(int index)
        {
            await Task.Yield();

            using var root = new Activity($"SqlClientConcurrentRoot{index}");
            root.Start();
            var traceId = FlowTestRequest.Begin(recorder, root, "GET", "/api/test");

            await QueryUserWithRawCommandAsync();
            return traceId;
        }
    }

    private async Task QueryUserWithRawCommandAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(UserByEmailSql, connection);
        command.Parameters.AddWithValue("email", SqlServerContainerFixture.UserEmail);
        await command.ExecuteScalarAsync();
    }

    private async Task QueryUserWithEntityFrameworkCoreAsync(NetApiFlowOptions options)
    {
        var interceptor = new FlowDbCommandInterceptor(Options.Create(options));
        var contextOptions = new DbContextOptionsBuilder<SqlServerTestDbContext>()
            .UseSqlServer(fixture.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        await using var context = new SqlServerTestDbContext(contextOptions);
        await context.Users
            .Where(user => user.Email == SqlServerContainerFixture.UserEmail)
            .Select(user => user.Id)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Every listener is returned so the test disposes it before the next one runs. A
    /// <see cref="DiagnosticListener"/> subscription is process-wide, so one left alive past its
    /// test keeps writing steps into whatever trace is current, and a test asserting that nothing
    /// was recorded would see the previous test's listener instead of its own.
    /// </summary>
    private static Arrangement Arrange(NetApiFlowOptions options)
    {
        var wrappedOptions = Options.Create(options);
        var recorder = new FlowRecorder(wrappedOptions);
        var listener = new FlowActivityListener(recorder, wrappedOptions, NullLoggerFactory.Instance);
        var sqlClientListener = new SqlClientCommandListener(wrappedOptions, NullLogger<SqlClientCommandListener>.Instance);
        sqlClientListener.Start();

        var root = new Activity("SqlClientInteropRoot");
        root.Start();
        var traceId = FlowTestRequest.Begin(recorder, root, "GET", "/api/test");

        return new Arrangement(recorder, listener, sqlClientListener, root, traceId);
    }

    private sealed record Arrangement(
        FlowRecorder Recorder,
        FlowActivityListener Listener,
        SqlClientCommandListener SqlClientListener,
        Activity Root,
        string TraceId) : IDisposable
    {
        public void Dispose()
        {
            SqlClientListener.Dispose();
            Listener.Dispose();
            Root.Dispose();
        }
    }

    private static List<FlowSpan> DatabaseSpansOf(FlowRecorder recorder, string traceId) =>
        [.. recorder.GetActiveTrace(traceId)!.Spans.Where(span => span.Type == ComponentType.Database)];
}

public sealed class SqlServerTestUser
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;
}

public sealed class SqlServerTestDbContext(DbContextOptions<SqlServerTestDbContext> options) : DbContext(options)
{
    public DbSet<SqlServerTestUser> Users => Set<SqlServerTestUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<SqlServerTestUser>().ToTable("Users");
}
