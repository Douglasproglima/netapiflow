namespace NetApiFlow;

/// <summary>Ecosystem <c>ActivitySource</c> instances NetApiFlow also listens to (ADR-0012). Opt-in, <see cref="None"/> by default.</summary>
[Flags]
public enum ExternalActivitySource
{
    /// <summary>No external source is observed. Default.</summary>
    None = 0,

    /// <summary>Outbound calls from <see cref="System.Net.Http.HttpClient"/>, mapped to a <see cref="ComponentType.Http"/> span.</summary>
    HttpClient = 1,
}
