using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

/// <summary>
/// Verifies <see cref="IFlowTenantResolver"/> is wired into <c>NetApiFlowMiddleware</c> (tarefa
/// 3.8, D-21): when registered, its result becomes the trace's <c>tenant</c> tag; with nothing
/// registered, no tag is added.
/// </summary>
public class TenantResolverIntegrationTests
{
    private sealed class FixedTenantResolver : IFlowTenantResolver
    {
        public string? Resolve(HttpContext context) => "acme";
    }

    private static IHost CreateHost(bool registerResolver)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    if (registerResolver)
                    {
                        services.AddSingleton<IFlowTenantResolver, FixedTenantResolver>();
                    }

                    services.AddNetApiFlow(o => o.IncludePaths("/**"));
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGet("/api/ping", () => "pong"));
                });
            })
            .Start();
    }

    [Fact]
    public async Task WithResolverRegistered_TraceGetsTenantTag()
    {
        using var host = CreateHost(registerResolver: true);
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        FlowTrace? completed = null;
        recorder.TraceCompleted += trace => completed = trace;

        var client = host.GetTestServer().CreateClient();
        await client.GetAsync("/api/ping");

        completed.ShouldNotBeNull();
        completed.Tags["tenant"].ShouldBe("acme");
    }

    [Fact]
    public async Task WithoutResolverRegistered_TraceHasNoTenantTag()
    {
        using var host = CreateHost(registerResolver: false);
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        FlowTrace? completed = null;
        recorder.TraceCompleted += trace => completed = trace;

        var client = host.GetTestServer().CreateClient();
        await client.GetAsync("/api/ping");

        completed.ShouldNotBeNull();
        completed.Tags.Keys.ShouldNotContain("tenant");
    }
}
