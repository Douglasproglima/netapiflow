namespace NetApiFlow.Instrumentation;

/// <summary>
/// OTel semantic convention tag names the native Npgsql <see cref="System.Diagnostics.ActivitySource"/>
/// produces. Only the ones NetApiFlow reads are listed; the driver emits several others that are
/// deliberately never recorded.
/// </summary>
internal static class NpgsqlActivityTagNames
{
    public const string SourceName = "Npgsql";
    public const string Statement = "db.statement";
}
