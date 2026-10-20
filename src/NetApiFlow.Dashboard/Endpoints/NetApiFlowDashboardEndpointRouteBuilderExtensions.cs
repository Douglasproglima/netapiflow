using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using NetApiFlow.Dashboard.StaticFiles;

namespace NetApiFlow.Dashboard.Endpoints;

/// <summary>Mounts the whole dashboard: the REST/SSE API and the embedded Angular UI (ADR-0005).</summary>
public static class NetApiFlowDashboardEndpointRouteBuilderExtensions
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    /// <summary>
    /// Serves the dashboard at <paramref name="basePath"/> (default <c>/flow</c>): the REST/SSE API
    /// under <c>{basePath}/api</c>, and the compiled Angular client everywhere else under
    /// <paramref name="basePath"/>, with a SPA fallback to <c>index.html</c> for any route that
    /// matches neither a static asset nor an API route (risco R-03). Chain <c>RequireAuthorization</c>
    /// on the result to protect every dashboard route with the same policy (ADR-0006, risco R-09).
    /// </summary>
    public static IEndpointConventionBuilder UseNetApiFlowDashboard(this IEndpointRouteBuilder endpoints, string basePath = "/flow")
    {
        var apiGroup = endpoints.MapNetApiFlowDashboardApi(basePath);

        var fileProvider = DashboardEmbeddedFiles.CreateFileProvider();
        var indexHtmlCache = new DashboardIndexHtmlCache(fileProvider);

        var staticGroup = endpoints.MapGroup(basePath);
        staticGroup.AddEndpointFilter(DashboardAccessGate.EnforceAsync);
        staticGroup.MapGet(
            "/{**path}",
            (HttpContext context, string? path) =>
                ServeStaticOrFallback(context, path ?? string.Empty, basePath, fileProvider, indexHtmlCache));

        return new CompositeEndpointConventionBuilder(apiGroup, staticGroup);
    }

    private static IResult ServeStaticOrFallback(
        HttpContext context,
        string path,
        string basePath,
        IFileProvider fileProvider,
        DashboardIndexHtmlCache indexHtmlCache)
    {
        var fileInfo = fileProvider.GetFileInfo(path);
        if (fileInfo.Exists && !fileInfo.IsDirectory)
        {
            var contentType = ContentTypeProvider.TryGetContentType(path, out var resolved)
                ? resolved
                : "application/octet-stream";

            return Results.File(fileInfo.CreateReadStream(), contentType);
        }

        var fullBasePath = $"{context.Request.PathBase}{basePath}/";
        return Results.Content(indexHtmlCache.GetHtml(fullBasePath), "text/html");
    }
}
