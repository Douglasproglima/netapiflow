using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetApiFlow.Dashboard;

internal sealed partial class DashboardFallbackPolicyWarning(
    IHostApplicationLifetime lifetime,
    IOptions<AuthorizationOptions> authorizationOptions,
    IOptions<NetApiFlowOptions> flowOptions,
    IEnumerable<EndpointDataSource> endpointDataSources,
    ILogger<DashboardFallbackPolicyWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(WarnWhenTheDashboardWouldBeChallenged);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void WarnWhenTheDashboardWouldBeChallenged()
    {
        try
        {
            if (authorizationOptions.Value.FallbackPolicy is null)
            {
                return;
            }

            var basePath = flowOptions.Value.DashboardBasePath;
            if (DashboardEndpoints(basePath).Any(endpoint => !DeclaresItsOwnAccess(endpoint)))
            {
                Log.DashboardWouldBeChallenged(logger, basePath);
            }
        }
        catch (Exception exception)
        {
            Log.CheckFailed(logger, exception);
        }
    }

    private static bool DeclaresItsOwnAccess(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
        || endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null;

    private IEnumerable<RouteEndpoint> DashboardEndpoints(string basePath) =>
        endpointDataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => IsUnder(endpoint.RoutePattern.RawText, basePath));

    private static bool IsUnder(string? route, string basePath) =>
        route is not null
        && (route.Equals(basePath, StringComparison.OrdinalIgnoreCase)
            || route.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase));

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "NetApiFlow.Dashboard: the application has an authorization FallbackPolicy and the dashboard routes under {BasePath} carry neither AllowAnonymous() nor RequireAuthorization(), so the authorization middleware answers with the challenge of its authentication scheme instead of serving the dashboard. In Development, chain AllowAnonymous() on MapNetApiFlowDashboard(). Outside Development, chain RequireAuthorization(\"YourPolicy\") alone, or chain AllowAnonymous() and register an IFlowAccessFilter. Chaining AllowAnonymous() together with RequireAuthorization() authorizes nothing.")]
        public static partial void DashboardWouldBeChallenged(ILogger logger, string basePath);

        [LoggerMessage(Level = LogLevel.Debug, Message = "NetApiFlow.Dashboard: the check for the authorization FallbackPolicy failed and was skipped.")]
        public static partial void CheckFailed(ILogger logger, Exception exception);
    }
}
