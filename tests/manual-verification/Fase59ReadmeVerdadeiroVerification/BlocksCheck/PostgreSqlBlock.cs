using NetApiFlow.Storage.PostgreSql;

public static class PostgreSqlBlock
{
    public static void Register(WebApplicationBuilder builder, string connectionString)
    {
        builder.Services.AddNetApiFlowPostgreSqlStorage(
            connectionString,
            options => options.TablePrefix = "netapiflow_");
    }
}
