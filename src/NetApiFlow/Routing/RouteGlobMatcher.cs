namespace NetApiFlow.Routing;

/// <summary>
/// Matches a request path against a route glob pattern. <c>*</c> matches exactly one path segment;
/// <c>**</c> matches any number of segments, including zero (see D-08 in docs/plano/decisoes.md).
/// </summary>
internal static class RouteGlobMatcher
{
    public static bool Matches(string pattern, string path)
    {
        var patternSegments = Split(pattern);
        var pathSegments = Split(path);
        return MatchesSegments(patternSegments, 0, pathSegments, 0);
    }

    private static string[] Split(string value) =>
        value.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool MatchesSegments(string[] pattern, int patternIndex, string[] path, int pathIndex)
    {
        while (true)
        {
            if (patternIndex == pattern.Length)
            {
                return pathIndex == path.Length;
            }

            var segment = pattern[patternIndex];

            if (segment == "**")
            {
                for (var skip = 0; skip <= path.Length - pathIndex; skip++)
                {
                    if (MatchesSegments(pattern, patternIndex + 1, path, pathIndex + skip))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (pathIndex == path.Length)
            {
                return false;
            }

            if (segment != "*" && !string.Equals(segment, path[pathIndex], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            patternIndex++;
            pathIndex++;
        }
    }
}
