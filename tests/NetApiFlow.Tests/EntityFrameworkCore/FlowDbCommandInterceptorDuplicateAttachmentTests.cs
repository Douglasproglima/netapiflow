using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.EntityFrameworkCore;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.EntityFrameworkCore;

public class FlowDbCommandInterceptorDuplicateAttachmentTests
{
    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();
    }

    private sealed class Order
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    private sealed class RecordingHarness : IDisposable
    {
        private readonly Activity _root;
        private readonly FlowActivityListener _listener;

        public RecordingHarness()
        {
            var options = Options.Create(new NetApiFlowOptions());
            Recorder = new FlowRecorder(options);
            _listener = new FlowActivityListener(Recorder, options, NullLoggerFactory.Instance);
            _root = new Activity("TestRoot");
            _root.Start();
            TraceId = _root.TraceId.ToString();
            Recorder.TryStartTrace(TraceId, "GET", "/api/test", DateTime.UtcNow);
        }

        public FlowRecorder Recorder { get; }

        public string TraceId { get; }

        public IReadOnlyList<FlowSpan> DatabaseSpans =>
            [.. Recorder.GetActiveTrace(TraceId)!.Spans.Where(s => s.Type == ComponentType.Database)];

        public void Dispose()
        {
            _root.Stop();
            _root.Dispose();
            _listener.Dispose();
        }
    }

    private static FlowDbCommandInterceptor CreateInterceptor() =>
        new(Options.Create(new NetApiFlowOptions()));

    private static async Task CreateSchemaAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).Options;
        await using var context = new TestDbContext(options);
        await context.Database.EnsureCreatedAsync();
    }

    [Fact]
    public async Task Query_WithInterceptorAttachedTwiceToTheSameOptions_RecordsASingleClosedSpan()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        await CreateSchemaAsync(connection);

        var interceptor = CreateInterceptor();
        var dbOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .AddInterceptors(interceptor)
            .Options;

        using var harness = new RecordingHarness();
        await using (var context = new TestDbContext(dbOptions))
        {
            await context.Orders.ToListAsync();
        }

        harness.DatabaseSpans.Count.ShouldBe(1);
        harness.DatabaseSpans[0].Status.ShouldBe(FlowStatus.Success);
    }

    [Fact]
    public async Task Query_WithInterceptorAttachedInBothAddDbContextAndAddDbContextFactory_RecordsASingleClosedSpan()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        await CreateSchemaAsync(connection);

        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new NetApiFlowOptions()));
        services.AddNetApiFlowEntityFrameworkCore();
        services.AddDbContext<TestDbContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetRequiredService<FlowDbCommandInterceptor>()));
        services.AddDbContextFactory<TestDbContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetRequiredService<FlowDbCommandInterceptor>()));

        await using var provider = services.BuildServiceProvider();

        using var harness = new RecordingHarness();
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Orders.ToListAsync();
        }

        harness.DatabaseSpans.Count.ShouldBe(1);
        harness.DatabaseSpans[0].Status.ShouldBe(FlowStatus.Success);
    }

    [Fact]
    public async Task Query_WithInterceptorAttachedOnce_RecordsASingleClosedSpan()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        await CreateSchemaAsync(connection);

        var dbOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(CreateInterceptor())
            .Options;

        using var harness = new RecordingHarness();
        await using (var context = new TestDbContext(dbOptions))
        {
            await context.Orders.ToListAsync();
        }

        harness.DatabaseSpans.Count.ShouldBe(1);
        harness.DatabaseSpans[0].Status.ShouldBe(FlowStatus.Success);
    }
}
