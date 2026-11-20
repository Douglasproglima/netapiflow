namespace NetApiFlow;

/// <summary>Ecosystem <c>ActivitySource</c> instances NetApiFlow also listens to. Opt-in, <see cref="None"/> by default.</summary>
[Flags]
public enum ExternalActivitySource
{
    /// <summary>No external source is observed. Default.</summary>
    None = 0,

    /// <summary>Outbound calls from <see cref="System.Net.Http.HttpClient"/>, mapped to a <see cref="ComponentType.Http"/> span.</summary>
    HttpClient = 1,

    /// <summary>
    /// SQL commands from Npgsql's own <c>ActivitySource</c>, mapped to a <see cref="ComponentType.Database"/>
    /// span, recorded as a child of the step that issued the command. Dropped only when a
    /// <see cref="ComponentType.Database"/> step already wraps the call (the one
    /// <c>NetApiFlow.EntityFrameworkCore</c> opens), checked through
    /// <see cref="System.Diagnostics.Activity.Parent"/>, so a command is never recorded twice.
    /// </summary>
    Npgsql = 2,

    /// <summary>
    /// SQL commands from Microsoft.Data.SqlClient, mapped to a <see cref="ComponentType.Database"/>
    /// span, so data access outside EF Core (Dapper, a raw command) is still recorded as a child of
    /// the step that issued it. Dropped under the same rule as <see cref="Npgsql"/>: only when a
    /// <see cref="ComponentType.Database"/> step already wraps the command.
    /// Needs a driver that publishes its diagnostic payload as a key/value list, which
    /// Microsoft.Data.SqlClient does from 6.0; an older driver records nothing here.
    /// </summary>
    SqlClient = 4,
}
