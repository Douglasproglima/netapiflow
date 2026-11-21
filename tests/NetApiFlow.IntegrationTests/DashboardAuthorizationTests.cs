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
using NetApiFlow.Domain;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

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

    [Fact]
    public async Task OutsideDevelopment_WithRequireAuthorizationAndAllowAnonymousChained_NoRouteIsServedAndNoTraceIsDeleted()
    {
        using var host = await StartHostAsync(
            Environments.Production,
            configureServices: DenyEveryoneAuthorization,
            configureDashboard: builder => builder.RequireAuthorization(DenyEveryonePolicy).AllowAnonymous());
        var store = host.Services.GetRequiredService<ITraceStore>();
        await store.SaveAsync(StoredTrace("kept-trace"), [], CancellationToken.None);
        var client = host.GetTestServer().CreateClient();

        (await client.GetAsync("/flow/")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/flow/api/config")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/flow/api/traces")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync("/flow/api/traces")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await store.GetAsync("kept-trace", CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task OutsideDevelopment_WithRequireAuthorizationAndTheRequiredClaim_RoutesAreServed()
    {
        using var host = await StartHostAsync(
            Environments.Production,
            configureServices: services => services
                .AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, AdminClaimAuthenticationHandler>("Test", _ => { })
                .Services
                .AddAuthorization(options => options.AddPolicy("RequiresAdmin", policy => policy.RequireClaim("role", "admin"))),
            configureDashboard: builder => builder.RequireAuthorization("RequiresAdmin"));
        var client = host.GetTestServer().CreateClient();

        (await client.GetAsync("/flow/api/config")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/flow/api/traces")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OutsideDevelopment_WithAllowAnonymousAlone_EveryRouteReturns404()
    {
        using var host = await StartHostAsync(
            Environments.Production,
            configureDashboard: builder => builder.AllowAnonymous());
        var client = host.GetTestServer().CreateClient();

        (await client.GetAsync("/flow/api/config")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync("/flow/api/traces")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.OK)]
    [InlineData(false, HttpStatusCode.Forbidden)]
    public async Task OutsideDevelopment_WithBothChainedAndAFlowAccessFilter_TheFilterDecides(bool filterAllows, HttpStatusCode expected)
    {
        using var host = await StartHostAsync(
            Environments.Production,
            configureServices: services =>
            {
                DenyEveryoneAuthorization(services);
                services.AddSingleton<IFlowAccessFilter>(new FixedAccessFilter(filterAllows));
            },
            configureDashboard: builder => builder.RequireAuthorization(DenyEveryonePolicy).AllowAnonymous());
        var client = host.GetTestServer().CreateClient();

        (await client.GetAsync("/flow/api/config")).StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task InDevelopment_WithRequireAuthorizationAndAllowAnonymousChained_RoutesAreServed()
    {
        using var host = await StartHostAsync(
            Environments.Development,
            configureServices: DenyEveryoneAuthorization,
            configureDashboard: builder => builder.RequireAuthorization(DenyEveryonePolicy).AllowAnonymous());
        var client = host.GetTestServer().CreateClient();

        (await client.GetAsync("/flow/api/config")).StatusCode.ShouldBe(HttpStatusCode.OK);
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

    private const string DenyEveryonePolicy = "DenyEveryone";

    private static void DenyEveryoneAuthorization(IServiceCollection services) => services
        .AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, NoClaimsAuthenticationHandler>("Test", _ => { })
        .Services
        .AddAuthorization(options => options.AddPolicy(DenyEveryonePolicy, policy => policy.RequireAssertion(_ => false)));

    private static FlowTrace StoredTrace(string traceId) => new()
    {
        TraceId = traceId,
        Method = "GET",
        RouteTemplate = "/api/orders/{id}",
        HttpStatus = 200,
        Status = FlowStatus.Success,
        StartedAt = DateTime.UtcNow,
        EndedAt = DateTime.UtcNow,
        DurationMicros = 1_000,
        SpanCount = 0,
        Truncated = false,
        Tags = new Dictionary<string, string>(),
    };

    private sealed class DenyingAccessFilter : IFlowAccessFilter
    {
        public ValueTask<bool> AuthorizeAsync(Microsoft.AspNetCore.Http.HttpContext context) => new(false);
    }

    private sealed class FixedAccessFilter(bool allows) : IFlowAccessFilter
    {
        public ValueTask<bool> AuthorizeAsync(Microsoft.AspNetCore.Http.HttpContext context) => new(allows);
    }

    private sealed class AdminClaimAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim("role", "admin")], "Test");
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
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
