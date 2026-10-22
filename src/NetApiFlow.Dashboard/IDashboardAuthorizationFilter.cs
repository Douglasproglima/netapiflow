using Microsoft.AspNetCore.Http;

namespace NetApiFlow.Dashboard;

/// <summary>
/// Custom authorization check for every dashboard route (API, SSE and static UI), as an
/// alternative to <c>RequireAuthorization</c> (ADR-0006, risco R-09). Register an implementation
/// in DI to have it run before any request is served; returning <see langword="false"/> responds
/// with <c>403</c>.
/// </summary>
public interface IDashboardAuthorizationFilter
{
    /// <summary>Returns <see langword="false"/> to deny the request with a <c>403</c>.</summary>
    ValueTask<bool> AuthorizeAsync(HttpContext context);
}
