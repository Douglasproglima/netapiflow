using System.Text.RegularExpressions;

namespace NetApiFlow.Storage.Tests;

internal static partial class StorageSchemaScripts
{
    private const string DefaultTablePrefix = "netapiflow_";

    public static string PostgreSql(string tablePrefix) => Load("postgresql.sql", tablePrefix);

    public static string SqlServer(string tablePrefix) => Load("sqlserver.sql", tablePrefix);

    public static IReadOnlyList<string> SplitIntoBatches(string script) =>
        BatchSeparator().Split(script).Where(batch => !string.IsNullOrWhiteSpace(batch)).ToList();

    private static string Load(string fileName, string tablePrefix) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schema", fileName))
            .Replace(DefaultTablePrefix, tablePrefix, StringComparison.Ordinal);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BatchSeparator();
}
