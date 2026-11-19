using System.Text.RegularExpressions;

namespace NetApiFlow.Routing;

/// <summary>
/// Brings a route template to one canonical shape: a leading slash, never a trailing one.
/// <c>RoutePattern.RawText</c> carries whichever convention the route was declared with (a
/// Controller route attribute has no leading slash, a Minimal API route typically does), so the
/// same route ends up recorded under two different strings unless every writer and reader agrees
/// on one shape first.
/// </summary>
internal static partial class RouteTemplateNormalizer
{
    public static string Normalize(string routeTemplate)
    {
        if (string.IsNullOrEmpty(routeTemplate))
        {
            return "/";
        }

        var withLeadingSlash = routeTemplate[0] == '/' ? routeTemplate : "/" + routeTemplate;
        return withLeadingSlash.Length > 1 && withLeadingSlash[^1] == '/'
            ? withLeadingSlash[..^1]
            : withLeadingSlash;
    }

    /// <summary>
    /// A route template's comparison key: same slash normalization as <see cref="Normalize"/>,
    /// plus every parameter stripped down to its bare name. <c>/api/orders/{id:int}</c> and
    /// <c>/api/orders/{id}</c> are the same route to a caller matching an OpenAPI document (which
    /// never carries ASP.NET routing constraints) against a recorded <c>RawText</c> (which
    /// always does); comparing the raw strings makes that route look dead. Never used for
    /// display: the caller keeps showing the original, constraint and all, from
    /// <see cref="Normalize"/> or the untouched source.
    /// </summary>
    public static string NormalizeForComparison(string routeTemplate)
    {
        return RouteParameterPattern().Replace(Normalize(routeTemplate), match =>
        {
            var inner = match.Groups[1].Value;
            if (inner.Length > 0 && inner[0] == '*')
            {
                inner = inner[1..];
            }

            var colonIndex = inner.IndexOf(':');
            var name = colonIndex >= 0 ? inner[..colonIndex] : inner;
            return $"{{{name.TrimEnd('?')}}}";
        });
    }

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex RouteParameterPattern();
}
