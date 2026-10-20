using Microsoft.Extensions.FileProviders;

namespace NetApiFlow.Dashboard.StaticFiles;

/// <summary>
/// Reads the embedded <c>index.html</c> once, then rewrites and caches one copy per distinct base
/// path (risco R-03): the same dashboard assembly can be mounted at any path, on any host,
/// possibly behind a reverse proxy's <c>PathBase</c>, so <c>&lt;base href&gt;</c> cannot be a
/// build-time constant.
/// </summary>
internal sealed class DashboardIndexHtmlCache
{
    private const string OriginalBaseHrefTag = "<base href=\"/\">";

    private readonly Lazy<string> _originalHtml;
    private readonly Dictionary<string, string> _rewrittenByBasePath = [];
    private readonly object _lock = new();

    public DashboardIndexHtmlCache(IFileProvider fileProvider)
    {
        _originalHtml = new Lazy<string>(() =>
        {
            using var stream = fileProvider.GetFileInfo("index.html").CreateReadStream();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });
    }

    /// <param name="fullBasePath">The dashboard's base path as the browser must address it: <c>PathBase + DashboardPath + "/"</c>.</param>
    public string GetHtml(string fullBasePath)
    {
        lock (_lock)
        {
            if (_rewrittenByBasePath.TryGetValue(fullBasePath, out var cached))
            {
                return cached;
            }

            var rewritten = _originalHtml.Value.Replace(
                OriginalBaseHrefTag,
                $"<base href=\"{fullBasePath}\">",
                StringComparison.Ordinal);

            _rewrittenByBasePath[fullBasePath] = rewritten;
            return rewritten;
        }
    }
}
