using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NetApiFlow.Middleware;

namespace NetApiFlow.DependencyInjection;

internal sealed class FlowResourceFilterMvcOptionsSetup(IOptions<NetApiFlowOptions> options) : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions mvcOptions)
    {
        if (options.Value.CapturePrePipelineSteps)
        {
            mvcOptions.Filters.Add<FlowResourceFilter>();
        }
    }
}
