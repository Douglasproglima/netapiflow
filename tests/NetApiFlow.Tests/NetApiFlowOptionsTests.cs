using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetApiFlow.Configuration;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests;

public class NetApiFlowOptionsTests
{
    private static IOptions<NetApiFlowOptions> BuildOptions(Dictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddNetApiFlowOptions();

        return services.BuildServiceProvider().GetRequiredService<IOptions<NetApiFlowOptions>>();
    }

    [Fact]
    public void Value_WithDefaultConfiguration_UsesDocumentedDefaults()
    {
        var options = BuildOptions([]).Value;

        options.Enabled.ShouldBeTrue();
        options.SlowThreshold.ShouldBe(TimeSpan.FromMilliseconds(250));
        options.CaptureSqlText.ShouldBeFalse();
        options.Retention.MaxTraces.ShouldBe(1000);
        options.Retention.MaxAge.ShouldBe(TimeSpan.FromHours(24));
        options.Limits.MaxSpansPerTrace.ShouldBe(300);
        options.ExternalSources.ShouldBe(ExternalActivitySource.None);
    }

    [Fact]
    public void Value_WithExternalSourcesHttpClientInConfiguration_BindsFlag()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            ["NetApiFlow:ExternalSources"] = "HttpClient",
        }).Value;

        options.ExternalSources.ShouldBe(ExternalActivitySource.HttpClient);
    }

    [Fact]
    public void Value_WithIncludeAndExcludePathsInConfiguration_BindsPatterns()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            ["NetApiFlow:IncludePaths:0"] = "/api/**",
            ["NetApiFlow:ExcludePaths:0"] = "/health/**",
        }).Value;

        options.IncludePatterns.ShouldBe(["/api/**"]);
        options.ExcludePatterns.ShouldBe(["/health/**"]);
    }

    [Fact]
    public void Value_WithRetentionMaxTracesZeroOrLess_ThrowsOptionsValidationException()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            ["NetApiFlow:Retention:MaxTraces"] = "0",
        });

        var exception = Should.Throw<OptionsValidationException>(() => _ = options.Value);
        exception.Message.ShouldContain("Retention.MaxTraces");
    }

    [Fact]
    public void Value_WithNonPositiveSlowThreshold_ThrowsOptionsValidationException()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            ["NetApiFlow:SlowThreshold"] = "00:00:00",
        });

        var exception = Should.Throw<OptionsValidationException>(() => _ = options.Value);
        exception.Message.ShouldContain("SlowThreshold");
    }
}
