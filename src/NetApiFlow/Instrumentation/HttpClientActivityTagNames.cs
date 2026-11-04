namespace NetApiFlow.Instrumentation;

/// <summary>
/// OTel semantic convention tag names the native <c>HttpClient</c> <see cref="System.Diagnostics.ActivitySource"/>
/// produces on <c>net9.0+</c>/<c>net10.0</c>, and that NetApiFlow backfills onto the same
/// <see cref="System.Diagnostics.Activity"/> on <c>net8.0</c> (ADR-0012, D-69).
/// </summary>
internal static class HttpClientActivityTagNames
{
    public const string SourceName = "System.Net.Http";
    public const string Method = "http.request.method";
    public const string UrlFull = "url.full";
    public const string StatusCode = "http.response.status_code";
    public const string ErrorType = "error.type";
}
