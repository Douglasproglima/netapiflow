using NetApiFlow.Storage.SqlServer;

public static class SqlServerBlock
{
    public static void Register(WebApplicationBuilder builder, string connectionString)
    {
        builder.Services.AddNetApiFlowSqlServerStorage(connectionString);
    }
}
