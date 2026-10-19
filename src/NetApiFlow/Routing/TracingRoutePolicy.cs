namespace NetApiFlow.Routing;

/// <summary>Decides whether a route template should be traced, given <see cref="NetApiFlowOptions"/>.</summary>
internal sealed class TracingRoutePolicy(NetApiFlowOptions options)
{
    public bool ShouldTrace(string routeTemplate)
    {
        var dashboardPattern = options.DashboardBasePath.TrimEnd('/') + "/**";
        if (RouteGlobMatcher.Matches(dashboardPattern, routeTemplate))
        {
            return false;
        }

        if (options.ExcludePatterns.Any(pattern => RouteGlobMatcher.Matches(pattern, routeTemplate)))
        {
            return false;
        }

        if (options.IncludePatterns.Count == 0)
        {
            return true;
        }

        return options.IncludePatterns.Any(pattern => RouteGlobMatcher.Matches(pattern, routeTemplate));
    }
}
