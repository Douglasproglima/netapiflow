namespace NetApiFlow.Routing;

/// <summary>
/// Brings a route template to one canonical shape: a leading slash, never a trailing one.
/// <c>RoutePattern.RawText</c> carries whichever convention the route was declared with (a
/// Controller route attribute has no leading slash, a Minimal API route typically does), so the
/// same route ends up recorded under two different strings unless every writer and reader agrees
/// on one shape first.
/// </summary>
internal static class RouteTemplateNormalizer
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
}
