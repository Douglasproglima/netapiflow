using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetApiFlow.Dashboard.Endpoints;

internal static class RequestOriginGuard
{
    private const string Subject = "the dashboard";
    private const string AllowedHostsKey = "AllowedHosts";
    private const string Wildcard = "*";
    private const string WildcardSubdomainPrefix = "*.";
    private const int MaxEchoedLength = 80;
    private const int DefaultHttpPort = 80;
    private const int DefaultHttpsPort = 443;

    public static string? Refusal(HttpContext context)
    {
        var request = context.Request;
        var services = context.RequestServices;

        if (!services.GetRequiredService<IHostEnvironment>().IsDevelopment()
            && request.Host.HasValue
            && !IsLoopbackOrDeclared(request.Host.Host, services.GetService<IConfiguration>()))
        {
            return HostRefusal(request.Host.Host);
        }

        return OriginMatchesHost(request) ? null : OriginRefusal(request);
    }

    private static bool IsLoopbackOrDeclared(string hostName, IConfiguration? configuration) =>
        IsLoopback(hostName) || IsDeclared(hostName, configuration);

    private static bool IsLoopback(string hostName) =>
        hostName.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(hostName.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

    private static bool IsDeclared(string hostName, IConfiguration? configuration)
    {
        var declared = configuration?[AllowedHostsKey]?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return declared is not null && declared.Any(entry => entry != Wildcard && Declares(entry, hostName));
    }

    private static bool Declares(string entry, string hostName) =>
        entry.StartsWith(WildcardSubdomainPrefix, StringComparison.Ordinal)
            ? hostName.EndsWith(entry[1..], StringComparison.OrdinalIgnoreCase)
            : hostName.Equals(entry, StringComparison.OrdinalIgnoreCase);

    private static bool OriginMatchesHost(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (origin.Length == 0)
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
            || (originUri.Scheme != Uri.UriSchemeHttp && originUri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var defaultPort = originUri.Scheme == Uri.UriSchemeHttps ? DefaultHttpsPort : DefaultHttpPort;
        return originUri.Host.Equals(request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && originUri.Port == (request.Host.Port ?? defaultPort);
    }

    private static string HostRefusal(string host) =>
        $"NetApiFlow refused this request. The Host '{Echo(host)}' is not a loopback name and is not declared. "
        + $"In Development {Subject} only answers to localhost, 127.0.0.1 and [::1], so that a web page on another site cannot reach it through DNS rebinding. "
        + $"To open it by another name, such as a LAN address, declare the name in the {AllowedHostsKey} setting, "
        + $"for example \"{AllowedHostsKey}\": \"localhost;{Echo(host)}\". A '{Wildcard}' does not count as declared.";

    private static string OriginRefusal(HttpRequest request) =>
        $"NetApiFlow refused this request. The Origin '{Echo(request.Headers.Origin.ToString())}' does not match the Host '{Echo(request.Host.ToString())}' that the application sees. "
        + $"If the application runs behind a reverse proxy that rewrites the Host, keep the original Host in the proxy (in nginx, proxy_set_header Host $http_host) "
        + $"or call UseForwardedHeaders with ForwardedHeaders.XForwardedHost before {Subject}.";

    private static string Echo(string value) =>
        new(value.Take(MaxEchoedLength).Select(character => char.IsControl(character) ? '?' : character).ToArray());
}
