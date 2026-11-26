using NetApiFlow;

public sealed class HeaderTenantResolver : IFlowTenantResolver
{
    public string? Resolve(HttpContext context) => context.Request.Headers["X-Tenant"].FirstOrDefault();
}

public static class TenantBlock
{
    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IFlowTenantResolver, HeaderTenantResolver>();
    }
}
