using Microsoft.AspNetCore.Http;

namespace NetApiFlow;

/// <summary>
/// Resolves the tenant a request belongs to, so it can be attached to the trace as the
/// <c>tenant</c> tag (seção 6.9 do prompt). Register an implementation in DI to opt in; with
/// nothing registered, traces carry no <c>tenant</c> tag.
/// </summary>
public interface IFlowTenantResolver
{
    /// <summary>Returns the tenant identifier for <paramref name="context"/>, or <see langword="null"/> if none applies.</summary>
    string? Resolve(HttpContext context);
}
