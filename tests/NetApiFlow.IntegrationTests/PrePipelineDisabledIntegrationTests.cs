using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class PrePipelineDisabledIntegrationTests
{
    private sealed class AllowAllFilter : IAsyncAuthorizationFilter
    {
        public Task OnAuthorizationAsync(AuthorizationFilterContext context) => Task.CompletedTask;
    }

    private static IHost StartHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Production);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddControllers().AddApplicationPart(typeof(PingController).Assembly);
                    services.AddNetApiFlow(options => options.IncludePaths("/api/**"));
                    services.Configure<MvcOptions>(options => options.Filters.Add(new AllowAllFilter()));
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .Start();

    [Fact]
    public async Task Request_WithCapturePrePipelineStepsOffByDefault_RecordsNoAuthorizationOrResourceFilterSpan()
    {
        using var host = StartHost();
        var spans = new ConcurrentBag<FlowSpan>();
        host.Services.GetRequiredService<FlowRecorder>().SpanCompleted += spans.Add;

        var response = await host.GetTestServer().CreateClient().GetAsync("/api/config-only-ping");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        spans.ShouldNotContain(span => span.Component == "AuthorizationFilters");
        spans.ShouldNotContain(span => span.Component == "ResourceFilters");
    }

    [Fact]
    public void MvcOptions_WithCapturePrePipelineStepsOffByDefault_HasNoResourceFilterEntry()
    {
        using var host = StartHost();

        var filters = host.Services.GetRequiredService<IOptions<MvcOptions>>().Value.Filters;

        filters.OfType<TypeFilterAttribute>().ShouldNotContain(filter => filter.ImplementationType.Name == "FlowResourceFilter");
    }

    [Fact]
    public void AuthorizationListener_WithCapturePrePipelineStepsOffByDefault_IsNotSubscribed()
    {
        using var host = StartHost();

        host.Services.GetRequiredService<FlowAuthorizationFilterListener>().IsSubscribed.ShouldBeFalse();
    }
}
