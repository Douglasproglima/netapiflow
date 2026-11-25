using NetApiFlow.Storage.PostgreSql;

public static class PostgreSqlNoDdlBlock
{
    public static void Register(WebApplicationBuilder builder, string connectionString)
    {
        builder.Services.AddNetApiFlowPostgreSqlStorage(
            connectionString,
            options => options.AutoCreateSchema = false);
    }
}
