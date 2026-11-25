using System.Diagnostics;

namespace NetApiFlow.Instrumentation;

internal static class NpgsqlActivityTagNames
{
    public const string SourceName = "Npgsql";
    public const string QueryText = "db.query.text";
    public const string LegacyStatement = "db.statement";
    private const string ConnectionOpenPrefix = "CONNECT ";

    public static string? StatementOf(Activity activity) =>
        activity.GetTagItem(QueryText) as string ?? activity.GetTagItem(LegacyStatement) as string;

    public static bool IsNamedLikeAConnectionOpen(Activity activity) =>
        activity.Source.Name == SourceName && activity.OperationName.StartsWith(ConnectionOpenPrefix, StringComparison.Ordinal);
}
