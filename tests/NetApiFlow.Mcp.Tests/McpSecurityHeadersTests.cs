using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Mcp;
using NetApiFlow.Mcp.Endpoints;
using NetApiFlow.TestSupport;
using Shouldly;
using Xunit;

namespace NetApiFlow.Mcp.Tests;

public class McpSecurityHeadersTests
{
    [Fact]
    public async Task AnAnsweredRequest_ComesWithTheSecurityHeaders()
    {
        using var host = await StartHostAsync(Environments.Development);
        using var response = await PostInitializeAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task ARefusedRequest_ComesWithTheSecurityHeaders()
    {
        using var host = await StartHostAsync(Environments.Development, denyEveryone: true);
        using var response = await PostInitializeAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task ARouteThatAnswers404OutsideDevelopment_ComesWithTheSecurityHeaders()
    {
        using var host = await StartHostAsync(Environments.Production);
        using var response = await PostInitializeAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        AssertSecurityHeaders(response);
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        foreach (var (name, value) in ExpectedSecurityHeaders.OnEveryResponse)
        {
            response.Headers.TryGetValues(name, out var values).ShouldBeTrue($"{name} is missing");
            values!.Single().ShouldBe(value);
        }

        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain(ExpectedSecurityHeaders.FrameAncestors);
    }

    private static Task<HttpResponseMessage> PostInitializeAsync(IHost host)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/flow/mcp")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        return host.GetTestServer().CreateClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    private static async Task<IHost> StartHostAsync(string environment, bool denyEveryone = false)
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(environment);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow();
                    services.AddNetApiFlowMcp();
                    if (denyEveryone)
                    {
                        services.AddSingleton<IFlowAccessFilter>(new DenyingAccessFilter());
                    }
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.UseNetApiFlowMcp());
                });
            })
            .StartAsync();
    }

    private sealed class DenyingAccessFilter : IFlowAccessFilter
    {
        public ValueTask<bool> AuthorizeAsync(HttpContext context) => new(false);
    }
}
