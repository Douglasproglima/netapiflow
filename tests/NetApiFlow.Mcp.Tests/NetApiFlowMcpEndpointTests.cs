using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using NetApiFlow.Mcp;
using NetApiFlow.Mcp.Endpoints;
using Shouldly;
using Xunit;

namespace NetApiFlow.Mcp.Tests;

/// <summary>Proves risco R-14: outside Development, every MCP route 404s unless the host configured <c>RequireAuthorization</c> or an <see cref="IFlowAccessFilter"/>.</summary>
public class NetApiFlowMcpEndpointTests
{
    [Fact]
    public async Task UseNetApiFlowMcp_InDevelopment_HandshakeSucceeds()
    {
        using var host = await StartHostAsync(Environments.Development);
        var httpClient = host.GetTestServer().CreateClient();

        await using var client = await ConnectAsync(httpClient);

        client.ServerInfo.ShouldNotBeNull();
    }

    [Fact]
    public async Task OutsideDevelopment_WithoutAnyConfiguration_EveryRouteReturns404()
    {
        using var host = await StartHostAsync(Environments.Production);
        var httpClient = host.GetTestServer().CreateClient();

        var response = await PostInitializeAsync(httpClient);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OutsideDevelopment_WithRequireAuthorizationAndMissingClaim_EveryRouteReturns403()
    {
        using var host = await StartHostAsync(
            Environments.Production,
            configureServices: services => services
                .AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, NoClaimsAuthenticationHandler>("Test", _ => { })
                .Services
                .AddAuthorization(options => options.AddPolicy("RequiresAdmin", policy => policy.RequireClaim("role", "admin"))),
            configureMcp: builder => builder.RequireAuthorization("RequiresAdmin"));
        var httpClient = host.GetTestServer().CreateClient();

        var response = await PostInitializeAsync(httpClient);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithFlowAccessFilterDenying_EveryRouteReturns403()
    {
        using var host = await StartHostAsync(
            Environments.Development,
            configureServices: services => services.AddSingleton<IFlowAccessFilter>(new DenyingAccessFilter()));
        var httpClient = host.GetTestServer().CreateClient();

        var response = await PostInitializeAsync(httpClient);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static Task<HttpResponseMessage> PostInitializeAsync(HttpClient httpClient)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/flow/mcp")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2026-01-01","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        return httpClient.SendAsync(request);
    }

    private static async Task<McpClient> ConnectAsync(HttpClient httpClient)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(httpClient.BaseAddress!, "/flow/mcp") },
            httpClient,
            ownsHttpClient: false);

        return await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);
    }

    private static async Task<IHost> StartHostAsync(
        string environmentName,
        Action<IServiceCollection>? configureServices = null,
        Action<IEndpointConventionBuilder>? configureMcp = null)
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(environmentName);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow();
                    services.AddNetApiFlowMcp();
                    services.AddAuthentication();
                    services.AddAuthorization();
                    configureServices?.Invoke(services);
                });
                webBuilder.Configure(app =>
                {
                    app.UseAuthentication();
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        var mcp = endpoints.UseNetApiFlowMcp();
                        configureMcp?.Invoke(mcp);
                    });
                });
            })
            .StartAsync();
    }

    private sealed class DenyingAccessFilter : IFlowAccessFilter
    {
        public ValueTask<bool> AuthorizeAsync(HttpContext context) => new(false);
    }

    private sealed class NoClaimsAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity("Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
