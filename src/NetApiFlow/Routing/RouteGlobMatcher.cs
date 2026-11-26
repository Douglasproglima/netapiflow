namespace NetApiFlow.Routing;

/// <summary>
/// Matches a request path against a route glob pattern. <c>*</c> matches exactly one path segment;
/// <c>**</c> matches any number of segments, including zero. Written by hand, because the core takes no
/// dependency for pattern matching.
/// </summary>
internal static class RouteGlobMatcher
{
    public static bool Matches(string pattern, string path) => Matches(Split(pattern), path);

    public static string[] Compile(string pattern) => Split(pattern);

    public static bool Matches(string[] patternSegments, string path) =>
        MatchesSegments(patternSegments, 0, Split(path), 0);

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
