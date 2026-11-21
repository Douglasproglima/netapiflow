namespace NetApiFlow.Storage.PostgreSql;

/// <summary>Configuration for <c>AddNetApiFlowPostgreSqlStorage</c>.</summary>
public sealed class PostgreSqlStorageOptions
{
    /// <summary>Prefix for every table this package creates and reads. Default: <c>netapiflow_</c>.</summary>
    public string TablePrefix { get; set; } = "netapiflow_";

    /// <summary>
    /// Creates and upgrades the tables on first use, with a <c>CREATE TABLE</c>, an
    /// <c>ALTER TABLE</c> and an <c>UPDATE</c> against the application's database. Default:
    /// <see langword="true"/>. With <see langword="false"/> nothing is created or changed and the
    /// tables must already exist; <c>schema.sql</c> in the package's repository folder creates them.
    /// </summary>
    public bool AutoCreateSchema { get; set; } = true;
}
