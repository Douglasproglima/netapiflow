namespace NetApiFlow.Routing;

/// <summary>Decides whether a route template should be traced, given <see cref="NetApiFlowOptions"/>.</summary>
internal sealed class TracingRoutePolicy
{
    private readonly string[] _dashboardPattern;
    private readonly string _openApiDocumentPath;
    private readonly string[][] _excludePatterns;
    private readonly string[][] _includePatterns;

    public TracingRoutePolicy(NetApiFlowOptions options)
    {
        _dashboardPattern = RouteGlobMatcher.Compile(options.DashboardBasePath.TrimEnd('/') + "/**");
        _openApiDocumentPath = options.OpenApiDocumentPath;
        _excludePatterns = [.. options.ExcludePatterns.Select(RouteGlobMatcher.Compile)];
        _includePatterns = [.. options.IncludePatterns.Select(RouteGlobMatcher.Compile)];
    }

    public bool ShouldTrace(string routeTemplate)
    {
        if (RouteGlobMatcher.Matches(_dashboardPattern, routeTemplate))
        {
            return false;
        }

        if (string.Equals(routeTemplate, _openApiDocumentPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_excludePatterns.Any(pattern => RouteGlobMatcher.Matches(pattern, routeTemplate)))
        {
            return false;
        }

        if (_includePatterns.Length == 0)
        {
            return true;
        }

        return _includePatterns.Any(pattern => RouteGlobMatcher.Matches(pattern, routeTemplate));
    }
}
