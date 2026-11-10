using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

namespace NetApiFlow.NpgsqlInteropSpike;

public sealed class PostgresSpikeFixture : IAsyncLifetime
{
    public static readonly string LeakProbeEmail = "spike-param-leak@example.com";

    public const int ConcurrentQueryCount = 8;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var dataSource = CreateDataSource();
        await using var context = SpikeDbContext.Create(dataSource);
        await context.Database.EnsureCreatedAsync();

        context.Users.Add(new SpikeUser { Email = LeakProbeEmail });
        for (var index = 0; index < ConcurrentQueryCount; index++)
        {
            context.Users.Add(new SpikeUser { Email = ConcurrentEmail(index) });
        }

        await context.SaveChangesAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public static string ConcurrentEmail(int index) => $"spike-concurrent-{index}@example.com";

    public NpgsqlDataSource CreateDataSource() => new NpgsqlDataSourceBuilder(ConnectionString).Build();

    public NpgsqlDataSource CreateMultiplexingDataSource()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionString) { Multiplexing = true }.ConnectionString;
        return new NpgsqlDataSourceBuilder(connectionString).Build();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresSpikeGroup : ICollectionFixture<PostgresSpikeFixture>
{
    public const string Name = "npgsql-interop-spike";
}

internal sealed class SpikeUser
{
    public int Id { get; set; }

    public string Email { get; set; } = "";
}

internal sealed class SpikeDbContext(DbContextOptions<SpikeDbContext> options) : DbContext(options)
{
    public DbSet<SpikeUser> Users => Set<SpikeUser>();

    public static SpikeDbContext Create(NpgsqlDataSource dataSource, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<SpikeDbContext>()
            .UseNpgsql(dataSource)
            .AddInterceptors(interceptors)
            .Options);
}
