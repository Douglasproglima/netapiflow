using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

/// <summary>
/// Verifies the dashboard's access gate (tarefa 5.4, risco R-09, ADR-0006, ADR-0015): outside
/// Development, every route (API and static UI alike) 404s unless the host configured
/// <c>RequireAuthorization</c> or an <see cref="IFlowAccessFilter"/>; with one configured and
/// denied, it 403s.
/// </summary>
public class DashboardAuthorizationTests
{
    [Fact]
    public async Task OutsideDevelopment_WithoutAnyConfiguration_EveryRouteReturns404()
    {
        using var host = await StartHostAsync(Environments.Production, configureDashboard: _ => { });
        var client = host.GetTestServer().CreateClient();

        (await client.GetAsync("/flow")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/flow/api/traces")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
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
            configureDashboard: builder => builder.RequireAuthorization("RequiresAdmin"));

        var client = host.GetTestServer().CreateClient();

        (await client.GetAsync("/flow")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/flow/api/traces")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithFlowAccessFilterDenying_EveryRouteReturns403()
    {
        using var host = await StartHostAsync(
            Environments.Development,
            configureServices: services => services.AddSingleton<IFlowAccessFilter>(new DenyingAccessFilter()),
            configureDashboard: _ => { });

        var client = host.GetTestServer().CreateClient();

        (await client.GetAsync("/flow")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/flow/api/traces")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<IHost> StartHostAsync(
        string environmentName,
        Action<IServiceCollection>? configureServices = null,
        Action<IEndpointConventionBuilder>? configureDashboard = null)
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
                    services.AddNetApiFlowDashboard();
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
                        var dashboard = endpoints.UseNetApiFlowDashboard();
                        configureDashboard?.Invoke(dashboard);
                    });
                });
            })
            .StartAsync();
    }

    private sealed class DenyingAccessFilter : IFlowAccessFilter
    {
        public ValueTask<bool> AuthorizeAsync(Microsoft.AspNetCore.Http.HttpContext context) => new(false);
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
