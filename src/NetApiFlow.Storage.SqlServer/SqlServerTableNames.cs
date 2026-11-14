using System.Text.RegularExpressions;

namespace NetApiFlow.Storage.SqlServer;

/// <summary>
/// Table identifiers can't be parameterized in SQL text, so the configured prefix is validated
/// once here instead of interpolated unchecked into every query.
/// </summary>
internal static partial class SqlServerTableNames
{
    public static (string Traces, string Spans) Resolve(string tablePrefix)
    {
        ValidateOrThrow(tablePrefix);
        return ($"dbo.{tablePrefix}traces", $"dbo.{tablePrefix}spans");
    }

    public static string ResolveDeployAnnotations(string tablePrefix)
    {
        ValidateOrThrow(tablePrefix);
        return $"dbo.{tablePrefix}deploy_annotations";
    }

    private static void ValidateOrThrow(string tablePrefix)
    {
        if (!ValidPrefix().IsMatch(tablePrefix))
        {
            throw new ArgumentException(
                $"NetApiFlow.Storage.SqlServer: TablePrefix '{tablePrefix}' is invalid. Use only letters, digits and underscores, starting with a letter or underscore.",
                nameof(tablePrefix));
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ValidPrefix();
}
