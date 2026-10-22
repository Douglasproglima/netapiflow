namespace NetApiFlow.Storage.PostgreSql;

/// <summary>Configuration for <c>AddNetApiFlowPostgreSqlStorage</c>.</summary>
public sealed class PostgreSqlStorageOptions
{
    /// <summary>Prefix for every table this package creates and reads. Default: <c>netapiflow_</c>.</summary>
    public string TablePrefix { get; set; } = "netapiflow_";
}
