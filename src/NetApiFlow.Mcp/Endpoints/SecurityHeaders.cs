using Microsoft.AspNetCore.Http;

namespace NetApiFlow.Mcp.Endpoints;

internal static class SecurityHeaders
{
    private const string FrameAncestorsPolicy = "frame-ancestors 'none'";

    public static void Apply(HttpResponse response)
    {
        var headers = response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";
        headers["Content-Security-Policy"] = FrameAncestorsPolicy;
    }
}
