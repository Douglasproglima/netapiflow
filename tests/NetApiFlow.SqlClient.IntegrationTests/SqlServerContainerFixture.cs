using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace NetApiFlow.SqlClient.IntegrationTests;

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var create = new SqlCommand(
            """
            CREATE TABLE Users (Id int IDENTITY(1,1) PRIMARY KEY, Email nvarchar(200) NOT NULL);
            INSERT INTO Users (Email) VALUES (@email);
            """,
            connection);
        create.Parameters.AddWithValue("email", UserEmail);
        await create.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public const string UserEmail = "user-0@example.com";
}

[CollectionDefinition(Name)]
public sealed class SqlServerTestGroup : ICollectionFixture<SqlServerContainerFixture>
{
    public const string Name = "sqlclient-interop";
}
