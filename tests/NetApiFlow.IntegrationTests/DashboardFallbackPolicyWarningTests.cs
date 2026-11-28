using System.Collections.Concurrent;
using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

public class DashboardFallbackPolicyWarningTests
{
    private const string FallbackPolicyKeyWord = "FallbackPolicy";

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Start_WithAFallbackPolicyAndNothingChainedOnTheDashboard_WarnsOnceNamingTheWayOut(string environment)
    {
        var logs = new CapturingLoggerProvider();

        using var host = await StartHostAsync(environment, withFallbackPolicy: true, logs, configureDashboard: _ => { });

        var warning = logs.Warnings().ShouldHaveSingleItem();
        warning.ShouldContain(FallbackPolicyKeyWord);
        warning.ShouldContain("challenge of the application's authentication scheme");
        warning.ShouldNotContain("401");
        warning.ShouldContain("AllowAnonymous()");
        warning.ShouldContain("RequireAuthorization");
        warning.ShouldContain("IFlowAccessFilter");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Start_WithAFallbackPolicyAndAllowAnonymousChained_DoesNotWarn(string environment)
    {
        var logs = new CapturingLoggerProvider();

        using var host = await StartHostAsync(environment, withFallbackPolicy: true, logs, configureDashboard: builder => builder.AllowAnonymous());

        logs.Warnings().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Start_WithAFallbackPolicyAndRequireAuthorizationChained_DoesNotWarn(string environment)
    {
        var logs = new CapturingLoggerProvider();

        using var host = await StartHostAsync(environment, withFallbackPolicy: true, logs, configureDashboard: builder => builder.RequireAuthorization("AllowedForTheTest"));

        logs.Warnings().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Start_WithoutAFallbackPolicy_DoesNotWarn(string environment)
    {
        var logs = new CapturingLoggerProvider();

        using var host = await StartHostAsync(environment, withFallbackPolicy: false, logs, configureDashboard: _ => { });

        logs.Warnings().ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_WithAFallbackPolicyAndNoDashboardMounted_DoesNotWarn()
    {
        var logs = new CapturingLoggerProvider();

        using var host = await StartHostAsync(Environments.Production, withFallbackPolicy: true, logs, configureDashboard: null);

        logs.Warnings().ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_InMinimalHostingWithAFallbackPolicyAndNothingChained_WarnsOnce()
    {
        var logs = new CapturingLoggerProvider();

        await using var app = await StartMinimalHostingAsync(logs, configureDashboard: _ => { });

        logs.Warnings().ShouldHaveSingleItem().ShouldContain(FallbackPolicyKeyWord);
    }

    [Fact]
    public async Task Start_InMinimalHostingWithAFallbackPolicyAndAllowAnonymousChained_DoesNotWarn()
    {
        var logs = new CapturingLoggerProvider();

        await using var app = await StartMinimalHostingAsync(logs, configureDashboard: builder => builder.AllowAnonymous());

        logs.Warnings().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(CookieAuthenticationDefaults.AuthenticationScheme, HttpStatusCode.Found)]
    [InlineData(BearerLikeScheme, HttpStatusCode.Unauthorized)]
    public async Task Start_WithAFallbackPolicyAndTheAuthenticationSchemeOfTheApp_WarnsAboutItsChallengeWhateverTheStatusCode(string scheme, HttpStatusCode challengeStatus)
    {
        var logs = new CapturingLoggerProvider();

        using var host = await StartHostAsync(
            Environments.Production,
            withFallbackPolicy: true,
            logs,
            configureDashboard: _ => { },
            addAuthentication: services => AddAuthenticationScheme(services, scheme));

        var warning = logs.Warnings().ShouldHaveSingleItem();
        warning.ShouldContain("challenge of the application's authentication scheme");
        warning.ShouldNotContain("401");
        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/flow/");
        response.StatusCode.ShouldBe(challengeStatus);
    }

    private const string BearerLikeScheme = "BearerLike";

    private static void AddAuthenticationScheme(IServiceCollection services, string scheme)
    {
        var authentication = services.AddAuthentication(scheme);
        if (scheme == CookieAuthenticationDefaults.AuthenticationScheme)
        {
            authentication.AddCookie();
        }
        else
        {
            authentication.AddScheme<AuthenticationSchemeOptions, ChallengesWith401Handler>(scheme, _ => { });
        }
    }

    private sealed class ChallengesWith401Handler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
    }

    private static async Task<WebApplication> StartMinimalHostingAsync(
        CapturingLoggerProvider logs,
        Action<IEndpointConventionBuilder> configureDashboard)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.AddProvider(logs);
        builder.Services.AddNetApiFlow();
        builder.Services.AddNetApiFlowDashboard();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        var app = builder.Build();
        app.UseAuthorization();
        configureDashboard(app.MapNetApiFlowDashboard());
        await app.StartAsync();
        return app;
    }

    private static async Task<IHost> StartHostAsync(
        string environment,
        bool withFallbackPolicy,
        CapturingLoggerProvider logs,
        Action<IEndpointConventionBuilder>? configureDashboard,
        Action<IServiceCollection>? addAuthentication = null)
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(environment);
                webBuilder.ConfigureLogging(logging => logging.AddProvider(logs));
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow();
                    services.AddNetApiFlowDashboard();
                    (addAuthentication ?? (collection => collection.AddAuthentication()))(services);
                    services.AddAuthorization(options =>
                    {
                        options.AddPolicy("AllowedForTheTest", policy => policy.RequireAssertion(_ => true));
                        if (withFallbackPolicy)
                        {
                            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
                        }
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        if (configureDashboard is not null)
                        {
                            configureDashboard(endpoints.MapNetApiFlowDashboard());
                        }
                    });
                });
            })
            .StartAsync();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _lines = new();

        public List<string> Warnings() =>
            _lines.Where(line => line.Level == LogLevel.Warning && line.Message.Contains("NetApiFlow.Dashboard", StringComparison.Ordinal))
                .Select(line => line.Message)
                .ToList();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_lines);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<(LogLevel Level, string Message)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
