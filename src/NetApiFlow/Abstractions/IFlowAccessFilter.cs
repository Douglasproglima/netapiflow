using Microsoft.AspNetCore.Http;

namespace NetApiFlow;

/// <summary>
/// Custom authorization check for a diagnostic surface (dashboard, MCP server), as an alternative
/// to <c>RequireAuthorization</c> (ADR-0006, ADR-0015). Register an implementation in DI to have it
/// run before any request is served; returning <see langword="false"/> responds with <c>403</c>.
/// </summary>
public interface IFlowAccessFilter
{
    /// <summary>Returns <see langword="false"/> to deny the request with a <c>403</c>.</summary>
    ValueTask<bool> AuthorizeAsync(HttpContext context);
}
