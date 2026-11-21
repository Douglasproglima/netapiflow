namespace NetApiFlow.TestSupport;

public static class ExpectedSecurityHeaders
{
    public const string FrameAncestors = "frame-ancestors 'none'";

    public const string DocumentPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; "
        + "font-src 'self'; connect-src 'self'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static readonly IReadOnlyDictionary<string, string> OnEveryResponse = new Dictionary<string, string>
    {
        ["X-Content-Type-Options"] = "nosniff",
        ["Referrer-Policy"] = "no-referrer",
        ["X-Frame-Options"] = "DENY",
    };
}
