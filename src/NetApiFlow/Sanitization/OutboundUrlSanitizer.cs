namespace NetApiFlow.Sanitization;

/// <summary>
/// Reduces an outbound URL to <c>host[:port]/path</c>, dropping query string and fragment
/// (ADR-0007, risco R-13), truncated to the same 300-character limit as error messages.
/// </summary>
internal static class OutboundUrlSanitizer
{
    private const int MaxLength = 300;

    public static string? SanitizeHostAndPath(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var hostAndPath = $"{uri.Authority}{uri.AbsolutePath}";
        return hostAndPath.Length > MaxLength ? hostAndPath[..MaxLength] : hostAndPath;
    }
}
