namespace NetApiFlow.Instrumentation;

/// <summary>
/// Names published by Microsoft.Data.SqlClient's <see cref="System.Diagnostics.DiagnosticListener"/>.
/// Unlike Npgsql, the driver writes no <see cref="System.Diagnostics.ActivitySource"/> of its own,
/// so the command events are the only thing to listen to.
/// </summary>
internal static class SqlClientDiagnosticNames
{
    public const string ListenerName = "SqlClientDiagnosticListener";
    public const string Component = "SqlClient";
    public const string CommandBefore = "Microsoft.Data.SqlClient.WriteCommandBefore";
    public const string CommandAfter = "Microsoft.Data.SqlClient.WriteCommandAfter";
    public const string CommandError = "Microsoft.Data.SqlClient.WriteCommandError";
    public const string OperationIdKey = "OperationId";
    public const string CommandKey = "Command";
    public const string ExceptionKey = "Exception";
}
