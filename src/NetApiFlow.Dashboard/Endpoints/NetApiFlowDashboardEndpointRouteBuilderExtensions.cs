using System.ComponentModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using NetApiFlow.Dashboard.StaticFiles;

namespace NetApiFlow.Dashboard.Endpoints;

/// <summary>Mounts the whole dashboard: the REST/SSE API and the embedded Angular UI.</summary>
public static class NetApiFlowDashboardEndpointRouteBuilderExtensions
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    /// <summary>
    /// Serves the dashboard at <paramref name="basePath"/> (default <c>/flow</c>): the REST/SSE API
    /// under <c>{basePath}/api</c>, and the compiled Angular client everywhere else under
    /// <paramref name="basePath"/>, with a SPA fallback to <c>index.html</c> for any route that
    /// matches neither a static asset nor an API route. Chain <c>RequireAuthorization</c>
    /// on the result to protect every dashboard route with the same policy. Outside
    /// Development, without an <see cref="IFlowAccessFilter"/>, every route answers 404 unless it
    /// requires authorization and does not allow anonymous access: chaining
    /// <c>AllowAnonymous</c> together with <c>RequireAuthorization</c> authorizes nothing.
    /// </summary>
    /// <param name="endpoints">The route builder to add the dashboard to.</param>
    /// <param name="basePath">The path the dashboard is served at.</param>
    /// <returns>A builder that applies a convention to every dashboard route.</returns>
    public static IEndpointConventionBuilder MapNetApiFlowDashboard(this IEndpointRouteBuilder endpoints, string basePath = "/flow")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(basePath);
        var apiGroup = endpoints.MapNetApiFlowDashboardApi(basePath);

        var fileProvider = DashboardEmbeddedFiles.CreateFileProvider();
        var indexHtmlCache = new DashboardIndexHtmlCache(
            fileProvider,
            endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<DashboardIndexHtmlCache>());

        var staticGroup = endpoints.MapGroup(basePath);
        staticGroup.AddEndpointFilter(DashboardAccessGate.EnforceAsync);
        staticGroup.ExcludeFromDescription();
        staticGroup.WithoutNetApiFlowTracking();
        staticGroup.MapGet(
            "/{**path}",
            (HttpContext context, string? path) =>
                ServeStaticOrFallback(context, path ?? string.Empty, basePath, fileProvider, indexHtmlCache));

        return new CompositeEndpointConventionBuilder(apiGroup, staticGroup);
    }

    /// <summary>The former name of <see cref="MapNetApiFlowDashboard"/>, which it forwards to.</summary>
    /// <param name="endpoints">The route builder to add the dashboard to.</param>
    /// <param name="basePath">The path the dashboard is served at.</param>
    /// <returns>A builder that applies a convention to every dashboard route.</returns>
    [Obsolete("Use MapNetApiFlowDashboard.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IEndpointConventionBuilder UseNetApiFlowDashboard(this IEndpointRouteBuilder endpoints, string basePath = "/flow") =>
        endpoints.MapNetApiFlowDashboard(basePath);

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

            if (contentType == "text/html")
            {
                SecurityHeaders.ApplyDocumentPolicy(context.Response);
            }

            return Results.File(fileInfo.CreateReadStream(), contentType);
        }

        SecurityHeaders.ApplyDocumentPolicy(context.Response);
        var fullBasePath = $"{context.Request.PathBase}{basePath}/";
        return Results.Content(indexHtmlCache.GetHtml(fullBasePath), "text/html");
    }
}
