using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace NetApiFlow.PrePipelineInteropSpike;

internal sealed class ControllersSampleWithAuthorizationAndResourceFiltersFactory : ControllersSampleFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
            services.Configure<MvcOptions>(options =>
            {
                options.Filters.Add<SpikeAuthorizationFilter>();
                options.Filters.Add<SpikeResourceFilter>();
            }));
    }
}
