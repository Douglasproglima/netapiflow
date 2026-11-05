using Testcontainers.PostgreSql;

namespace NetApiFlow.Storage.Tests;

/// <summary>One PostgreSQL container shared by every test in the collection, not per test.</summary>
public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlTestGroup : ICollectionFixture<PostgreSqlContainerFixture>
{
    public const string Name = "postgresql";
}
