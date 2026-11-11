using Microsoft.AspNetCore.Mvc.Testing;
using SampleApi.MinimalMediatR.Application;

namespace NetApiFlow.PrePipelineInteropSpike;

internal sealed class MinimalMediatRSampleFactory : WebApplicationFactory<GetOrderQuery>;
