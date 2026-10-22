using System.Text.RegularExpressions;

namespace NetApiFlow.Storage.PostgreSql;

/// <summary>
/// Table identifiers can't be parameterized in SQL text, so the configured prefix is validated
/// once here instead of interpolated unchecked into every query.
/// </summary>
internal static partial class PostgreSqlTableNames
{
    public static (string Traces, string Spans) Resolve(string tablePrefix)
    {
        if (!ValidPrefix().IsMatch(tablePrefix))
        {
            throw new ArgumentException(
                $"NetApiFlow.Storage.PostgreSql: TablePrefix '{tablePrefix}' is invalid. Use only letters, digits and underscores, starting with a letter or underscore.",
                nameof(tablePrefix));
        }

        return ($"{tablePrefix}traces", $"{tablePrefix}spans");
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ValidPrefix();
}
