namespace NetApiFlow.TestSupport;

public sealed record OriginAndHostCase(
    string Name,
    string Environment,
    string Host,
    string? Origin,
    string? AllowedHosts,
    string? RefusedBecause);

public static class OriginAndHostCases
{
    public const string Development = "Development";
    public const string Production = "Production";
    public const string HostRefusal = "host";
    public const string OriginRefusal = "origin";

    public static readonly IReadOnlyList<OriginAndHostCase> All =
    [
        new("dev localhost, no origin", Development, "localhost:5200", null, null, null),
        new("dev 127.0.0.1, no origin", Development, "127.0.0.1:5200", null, null, null),
        new("dev ::1, no origin", Development, "[::1]:5200", null, null, null),
        new("dev rebinding, foreign host without origin", Development, "evil.example", null, null, HostRefusal),
        new("dev foreign host with its own origin", Development, "evil.example", "http://evil.example", null, HostRefusal),
        new("dev localhost, same origin", Development, "localhost:5200", "http://localhost:5200", null, null),
        new("dev localhost, same origin over https", Development, "localhost:5200", "https://localhost:5200", null, null),
        new("dev localhost, foreign origin", Development, "localhost:5200", "http://evil.example", null, OriginRefusal),
        new("dev localhost, loopback origin on another port", Development, "localhost:5200", "http://localhost:3000", null, OriginRefusal),
        new("dev localhost, null origin", Development, "localhost:5200", "null", null, OriginRefusal),
        new("dev localhost, malformed origin", Development, "localhost:5200", "not a url", null, OriginRefusal),
        new("dev lan address, nothing declared", Development, "192.168.1.13:5200", null, null, HostRefusal),
        new("dev lan address, only the wildcard declared", Development, "192.168.1.13:5200", null, "*", HostRefusal),
        new("dev lan address, declared", Development, "192.168.1.13:5200", null, "localhost;192.168.1.13", null),
        new("dev lan address, declared, same origin", Development, "192.168.1.13:5200", "http://192.168.1.13:5200", "192.168.1.13", null),
        new("dev subdomain wildcard declared", Development, "phone.lan:5200", null, "*.lan", null),
        new("dev docker host, not declared", Development, "host.docker.internal:5200", null, null, HostRefusal),
        new("dev docker host, declared", Development, "host.docker.internal:5200", null, "host.docker.internal", null),
        new("prod any host, no origin", Production, "dash.example", null, null, null),
        new("prod same origin over https", Production, "dash.example", "https://dash.example", null, null),
        new("prod same origin with the default port", Production, "dash.example", "https://dash.example:443", null, null),
        new("prod same origin with a custom port", Production, "dash.example:8443", "https://dash.example:8443", null, null),
        new("prod foreign origin", Production, "dash.example", "https://evil.example", null, OriginRefusal),
        new("prod other port origin", Production, "dash.example:8443", "https://dash.example:9443", null, OriginRefusal),
        new("prod host rewritten by the proxy", Production, "host.docker.internal:5200", "http://localhost:8082", null, OriginRefusal),
    ];

    public static IEnumerable<object[]> Names => All.Select(testCase => new object[] { testCase.Name });

    public static OriginAndHostCase ByName(string name) => All.Single(testCase => testCase.Name == name);

    public static string MessageFragmentFor(string refusal) => refusal == HostRefusal ? "AllowedHosts" : "Origin";
}
