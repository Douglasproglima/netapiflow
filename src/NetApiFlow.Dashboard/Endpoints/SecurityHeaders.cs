using Microsoft.AspNetCore.Http;

namespace NetApiFlow.Dashboard.Endpoints;

internal static class SecurityHeaders
{
    private const string FrameAncestorsPolicy = "frame-ancestors 'none'";

    private const string DocumentPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; "
        + "font-src 'self'; connect-src 'self'; base-uri 'self'; object-src 'none'; form-action 'self'; frame-ancestors 'none'";

    public static void Apply(HttpResponse response)
    {
        var headers = response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";
        headers["Content-Security-Policy"] = FrameAncestorsPolicy;
    }

    public static void ApplyDocumentPolicy(HttpResponse response) =>
        response.Headers["Content-Security-Policy"] = DocumentPolicy;
}
