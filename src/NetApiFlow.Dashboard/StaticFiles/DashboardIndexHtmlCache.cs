using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace NetApiFlow.Dashboard.StaticFiles;

internal sealed partial class DashboardIndexHtmlCache
{
    private readonly Lazy<string> _originalHtml;
    private readonly Dictionary<string, string> _rewrittenByBasePath = [];
    private readonly object _lock = new();
    private readonly ILogger _logger;

    public DashboardIndexHtmlCache(IFileProvider fileProvider, ILogger logger)
    {
        _logger = logger;
        _originalHtml = new Lazy<string>(() =>
        {
            using var stream = fileProvider.GetFileInfo("index.html").CreateReadStream();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });
    }

    public string GetHtml(string fullBasePath)
    {
        lock (_lock)
        {
            if (_rewrittenByBasePath.TryGetValue(fullBasePath, out var cached))
            {
                return cached;
            }

            var original = _originalHtml.Value;
            var hasBaseTag = BaseTag().IsMatch(original);
            if (!hasBaseTag)
            {
                Log.BaseTagNotFound(_logger);
            }

            var rewritten = hasBaseTag ? BaseTag().Replace(original, _ => $"<base href=\"{fullBasePath}\">", 1) : original;

            _rewrittenByBasePath[fullBasePath] = rewritten;
            return rewritten;
        }
    }

    [GeneratedRegex(@"<base\s+href\s*=\s*(?:""/""|'/')\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BaseTag();

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "NetApiFlow.Dashboard: index.html has no <base href=\"/\"> tag to rewrite, so it is served as built and the dashboard cannot find its files when it is not mounted at the root of the site. The Angular build may have changed how it writes that tag.")]
        public static partial void BaseTagNotFound(ILogger logger);
    }
}
