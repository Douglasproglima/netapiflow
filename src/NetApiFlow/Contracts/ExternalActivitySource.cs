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
    /// span. Deduplicated against <c>NetApiFlow.EntityFrameworkCore</c>'s own span for the same
    /// command via <see cref="System.Diagnostics.Activity.Parent"/>, never both.
    /// </summary>
    Npgsql = 2,
}
