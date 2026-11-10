using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

namespace NetApiFlow.Npgsql.IntegrationTests;

public sealed class NpgsqlContainerFixture : IAsyncLifetime
{
    public const int ConcurrentQueryCount = 8;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DataSource = NpgsqlDataSource.Create(_container.GetConnectionString());

        await using var context = NpgsqlTestDbContext.Create(DataSource);
        await context.Database.EnsureCreatedAsync();

        for (var index = 0; index < ConcurrentQueryCount; index++)
        {
            context.Users.Add(new NpgsqlTestUser { Email = UserEmail(index) });
        }

        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    public static string UserEmail(int index) => $"user-{index}@example.com";
}

[CollectionDefinition(Name)]
public sealed class NpgsqlTestGroup : ICollectionFixture<NpgsqlContainerFixture>
{
    public const string Name = "npgsql-interop";
}

internal sealed class NpgsqlTestUser
{
    public int Id { get; set; }

    public string Email { get; set; } = "";
}

internal sealed class NpgsqlTestDbContext(DbContextOptions<NpgsqlTestDbContext> options) : DbContext(options)
{
    public DbSet<NpgsqlTestUser> Users => Set<NpgsqlTestUser>();

    public static NpgsqlTestDbContext Create(NpgsqlDataSource dataSource, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<NpgsqlTestDbContext>()
            .UseNpgsql(dataSource)
            .AddInterceptors(interceptors)
            .Options);
}
