using System.Text.RegularExpressions;

namespace NetApiFlow.Instrumentation;

/// <summary>
/// Derives "{CommandType} {MainTable}" from a SQL command text, never the parameter values.
/// Deliberately duplicates <c>FlowDbCommandInterceptor.DescribeCommand</c>: the core package
/// cannot depend on <c>NetApiFlow.EntityFrameworkCore</c>.
/// </summary>
internal static partial class SqlCommandDescriber
{
    public static string Describe(string commandText)
    {
        var typeMatch = CommandTypeRegex().Match(commandText);
        var commandType = typeMatch.Success ? typeMatch.Groups[1].Value.ToUpperInvariant() : "SQL";

        var tableMatch = MainTableRegex().Match(commandText);
        var table = tableMatch.Success ? tableMatch.Groups[1].Value : "Unknown";

        return $"{commandType} {table}";
    }

    [GeneratedRegex(@"^\s*(SELECT|INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex CommandTypeRegex();

    [GeneratedRegex(@"\b(?:FROM|INTO|UPDATE)\s+[`""\[]?(\w+)[`""\]]?", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex MainTableRegex();
}
