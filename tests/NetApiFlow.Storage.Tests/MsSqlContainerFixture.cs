using Testcontainers.MsSql;

namespace NetApiFlow.Storage.Tests;

/// <summary>One SQL Server container shared by every test in the collection (tarefa 6.5), not per test.</summary>
public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class MsSqlTestGroup : ICollectionFixture<MsSqlContainerFixture>
{
    public const string Name = "mssql";
}
