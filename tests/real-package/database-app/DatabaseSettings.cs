public sealed record DatabaseSettings(string Kind, string ConnectionString, string TablePrefix, string? SchemaScriptPath, bool AutoCreateSchema)
{
    public static DatabaseSettings FromEnvironment() =>
        new(
            Required("DATABASE_KIND"),
            Required("DATABASE_CONNECTION"),
            Required("DATABASE_TABLE_PREFIX"),
            Environment.GetEnvironmentVariable("DATABASE_SCHEMA_SQL"),
            Environment.GetEnvironmentVariable("DATABASE_AUTO_CREATE_SCHEMA") != "false");

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Set the environment variable {name}.");
}
