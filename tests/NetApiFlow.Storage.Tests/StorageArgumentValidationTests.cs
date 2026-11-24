using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Storage.PostgreSql;
using NetApiFlow.Storage.SqlServer;
using Shouldly;

namespace NetApiFlow.Storage.Tests;

public class StorageArgumentValidationTests
{
    private const string AnyConnectionString = "Host=localhost;Database=netapiflow";

    public static TheoryData<string, Action> NullServices => new()
    {
        { "PostgreSQL", () => NetApiFlowPostgreSqlServiceCollectionExtensions.AddNetApiFlowPostgreSqlStorage(null!, AnyConnectionString) },
        { "SQL Server", () => NetApiFlowSqlServerServiceCollectionExtensions.AddNetApiFlowSqlServerStorage(null!, AnyConnectionString) },
    };

    public static TheoryData<string, string?> MissingConnectionStrings => new()
    {
        { "PostgreSQL", null },
        { "PostgreSQL", "" },
        { "PostgreSQL", "   " },
        { "SQL Server", null },
        { "SQL Server", "" },
        { "SQL Server", "   " },
    };

    [Theory]
    [MemberData(nameof(NullServices))]
    public void AddStorage_WithNullServices_ThrowsNamingTheParameter(string database, Action call)
    {
        Should.Throw<ArgumentNullException>(call, database).ParamName.ShouldBe("services");
    }

    [Theory]
    [MemberData(nameof(MissingConnectionStrings))]
    public void AddStorage_WithoutAConnectionString_ThrowsAMessageThatSaysWhatToPass(string database, string? connectionString)
    {
        var services = new ServiceCollection();
        Action call = database == "PostgreSQL"
            ? () => services.AddNetApiFlowPostgreSqlStorage(connectionString!)
            : () => services.AddNetApiFlowSqlServerStorage(connectionString!);

        var exception = Should.Throw<ArgumentException>(call);

        exception.ParamName.ShouldBe("connectionString");
        exception.Message.ShouldContain(database);
        exception.Message.ShouldContain("connection string");
    }
}
