namespace NetApiFlow.Storage.SqlServer;

/// <summary>Configuration for <c>AddNetApiFlowSqlServerStorage</c>.</summary>
public sealed class SqlServerStorageOptions
{
    /// <summary>Prefix for every table this package creates and reads. Default: <c>netapiflow_</c>.</summary>
    public string TablePrefix { get; set; } = "netapiflow_";
}
