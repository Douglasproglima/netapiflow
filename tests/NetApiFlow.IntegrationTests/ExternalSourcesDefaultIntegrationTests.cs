using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

/// <summary>
/// The sources the runtime and the common drivers already emit are on while developing, and
/// nowhere else. Whatever the host states explicitly always wins, from configuration or from
/// code, including turning them back off.
/// </summary>
public class ExternalSourcesDefaultIntegrationTests
{
    /// <summary>
    /// Configuration sources are cleared, then only what a test asks for is added back. The
    /// ambient <c>appsettings.json</c> the test output inherits from the sample app already sets
    /// this very option, which would otherwise decide the answer instead of the code under test.
    /// </summary>
    private static ExternalActivitySource ResolveExternalSources(
        string environmentName,
        Action<NetApiFlowOptions>? configure = null,
        string? configuredValue = null)
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(configuration =>
            {
                configuration.Sources.Clear();
                if (configuredValue is not null)
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["NetApiFlow:ExternalSources"] = configuredValue });
                }
            })
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(environmentName);
                webBuilder.ConfigureServices(services => services.AddNetApiFlow(configure));
                webBuilder.Configure(_ => { });
            })
            .Start();

        return host.Services.GetRequiredService<IOptions<NetApiFlowOptions>>().Value.ExternalSources;
    }

    [Fact]
    public void Development_WithNothingStatedExplicitly_TurnsOnTheThreeNativeSources()
    {
        var sources = ResolveExternalSources(Environments.Development);

        sources.ShouldBe(
            ExternalActivitySource.HttpClient | ExternalActivitySource.Npgsql | ExternalActivitySource.SqlClient);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void OutsideDevelopment_WithNothingStatedExplicitly_StaysNone(string environmentName)
    {
        var sources = ResolveExternalSources(environmentName);

        sources.ShouldBe(ExternalActivitySource.None);
    }

    [Fact]
    public void Development_WithNoneStatedInCode_StaysNone()
    {
        var sources = ResolveExternalSources(
            Environments.Development,
            configure: options => options.ExternalSources = ExternalActivitySource.None);

        sources.ShouldBe(ExternalActivitySource.None);
    }

    [Fact]
    public void Development_WithOneSourceStatedInCode_KeepsOnlyThatOne()
    {
        var sources = ResolveExternalSources(
            Environments.Development,
            configure: options => options.ExternalSources = ExternalActivitySource.HttpClient);

        sources.ShouldBe(ExternalActivitySource.HttpClient);
    }

    [Fact]
    public void Development_WithAValueInConfiguration_KeepsTheConfiguredValue()
    {
        var sources = ResolveExternalSources(Environments.Development, configuredValue: "HttpClient");

        sources.ShouldBe(ExternalActivitySource.HttpClient);
    }

    /// <summary>
    /// A service collection outside a host has no <see cref="IHostEnvironment"/>. Requiring one
    /// would break every caller that registers NetApiFlow that way, a console app or a unit test
    /// among them, so the environment is resolved optionally and the value simply stays
    /// <see cref="ExternalActivitySource.None"/>.
    /// </summary>
    [Fact]
    public void WithoutAHostEnvironment_ResolvingOptions_StaysNoneInsteadOfThrowing()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddNetApiFlow();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<NetApiFlowOptions>>().Value.ExternalSources
            .ShouldBe(ExternalActivitySource.None);
    }

    [Fact]
    public void Development_WithNoneInConfiguration_StaysNone()
    {
        var sources = ResolveExternalSources(Environments.Development, configuredValue: "None");

        sources.ShouldBe(ExternalActivitySource.None);
    }
}
