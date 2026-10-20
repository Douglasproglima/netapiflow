using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetApiFlow.EntityFrameworkCore;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.EntityFrameworkCore;

public class FlowDbCommandInterceptorTests
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

    [Fact]
    public async Task Commands_WithCaptureSqlTextDisabled_NeverExposeParameterValuesInSpans()
    {
        var recorderOptions = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(recorderOptions);
        using var listener = new FlowActivityListener(recorder, recorderOptions);

        using var root = new Activity("TestRoot");
        root.Start();
        var traceId = root.TraceId.ToString();
        recorder.TryStartTrace(traceId, "GET", "/api/test", DateTime.UtcNow);

        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var interceptor = new FlowDbCommandInterceptor(Options.Create(new NetApiFlowOptions { CaptureSqlText = false }));
        var dbOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

        const string secret = "secret-customer-name";

        await using (var context = new TestDbContext(dbOptions))
        {
            await context.Database.EnsureCreatedAsync();
            context.Orders.Add(new Order { Id = 1, Name = secret });
            await context.SaveChangesAsync();

            var loaded = await context.Orders.Where(o => o.Name == secret).ToListAsync();
            loaded.Count.ShouldBe(1);
        }

        var spans = recorder.GetActiveTrace(traceId)!.Spans.Where(s => s.Type == ComponentType.Database).ToList();
        spans.ShouldNotBeEmpty();

        foreach (var span in spans)
        {
            span.Operation.ShouldNotContain(secret);
            span.Tags.Values.ShouldNotContain(v => v.Contains(secret, StringComparison.Ordinal));
            (span.ErrorMessage ?? string.Empty).ShouldNotContain(secret);
            span.Tags.Keys.ShouldNotContain("sql");
        }

        spans.ShouldContain(s => s.Operation.StartsWith("INSERT", StringComparison.Ordinal));
        spans.ShouldContain(s => s.Operation.StartsWith("SELECT", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("SELECT * FROM \"Orders\" WHERE \"Id\" = @p0", "SELECT Orders")]
    [InlineData("INSERT INTO \"Orders\" (\"Id\", \"Name\") VALUES (@p0, @p1)", "INSERT Orders")]
    [InlineData("UPDATE \"Orders\" SET \"Name\" = @p0 WHERE \"Id\" = @p1", "UPDATE Orders")]
    [InlineData("DELETE FROM \"Orders\" WHERE \"Id\" = @p0", "DELETE Orders")]
    public void DescribeCommand_ExtractsCommandTypeAndMainTable_WithoutParameterValues(string sql, string expected)
    {
        FlowDbCommandInterceptor.DescribeCommand(sql).ShouldBe(expected);
    }
}
