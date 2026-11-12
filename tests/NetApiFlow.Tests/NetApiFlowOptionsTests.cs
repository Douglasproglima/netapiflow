using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        options.TailSampling.Enabled.ShouldBeFalse();
        options.TailSampling.SuccessSampleRate.ShouldBe(0.1);
        options.Limits.MaxLogEntriesPerSpan.ShouldBe(20);
        options.Limits.MaxTagsPerSpan.ShouldBe(20);
        options.Limits.MaxTagValueLength.ShouldBe(4000);
        options.LogCorrelation.Enabled.ShouldBeFalse();
        options.LogCorrelation.MinLevel.ShouldBe(LogLevel.Warning);
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

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    public void Value_WithSuccessSampleRateOutOfRange_ThrowsOptionsValidationException(string rate)
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            ["NetApiFlow:TailSampling:SuccessSampleRate"] = rate,
        });

        var exception = Should.Throw<OptionsValidationException>(() => _ = options.Value);
        exception.Message.ShouldContain("TailSampling.SuccessSampleRate");
    }

    [Fact]
    public void Value_WithMaxLogEntriesPerSpanZeroOrLess_ThrowsOptionsValidationException()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            ["NetApiFlow:Limits:MaxLogEntriesPerSpan"] = "0",
        });

        var exception = Should.Throw<OptionsValidationException>(() => _ = options.Value);
        exception.Message.ShouldContain("Limits.MaxLogEntriesPerSpan");
    }

    [Fact]
    public void Value_WithMaxTagsPerSpanZeroOrLess_ThrowsOptionsValidationException()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            ["NetApiFlow:Limits:MaxTagsPerSpan"] = "0",
        });

        var exception = Should.Throw<OptionsValidationException>(() => _ = options.Value);
        exception.Message.ShouldContain("Limits.MaxTagsPerSpan");
    }

    [Fact]
    public void Value_WithMaxTagValueLengthZeroOrLess_ThrowsOptionsValidationException()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            ["NetApiFlow:Limits:MaxTagValueLength"] = "0",
        });

        var exception = Should.Throw<OptionsValidationException>(() => _ = options.Value);
        exception.Message.ShouldContain("Limits.MaxTagValueLength");
    }
}
