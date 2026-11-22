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
    public void Value_WithoutExplicitOpenApiDocumentPath_UsesTheMapOpenApiDefault()
    {
        BuildOptions([]).Value.OpenApiDocumentPath.ShouldBe("/openapi/v1.json");
    }

    [Fact]
    public void Value_WithOpenApiDocumentPathConfigured_UsesIt()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            ["NetApiFlow:OpenApiDocumentPath"] = "/swagger/v1/swagger.json",
        });

        options.Value.OpenApiDocumentPath.ShouldBe("/swagger/v1/swagger.json");
    }

    [Theory]
    [InlineData("openapi/v1.json")]
    [InlineData("")]
    public void Value_WithOpenApiDocumentPathNotRootRelative_ThrowsOptionsValidationException(string path)
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            ["NetApiFlow:OpenApiDocumentPath"] = path,
        });

        var exception = Should.Throw<OptionsValidationException>(() => _ = options.Value);
        exception.Message.ShouldContain("OpenApiDocumentPath");
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

    [Theory]
    [InlineData("NetApiFlow:IncludePaths:1", "IncludePaths")]
    [InlineData("NetApiFlow:ExcludePaths:1", "ExcludePaths")]
    public void Value_WithABlankPathPatternInConfiguration_ThrowsOptionsValidationExceptionNamingTheOption(string key, string option)
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            [key.Replace(":1", ":0", StringComparison.Ordinal)] = "/api/**",
            [key] = "   ",
        });

        var exception = Should.Throw<OptionsValidationException>(() => _ = options.Value);
        exception.Message.ShouldContain(option);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithANullEmptyOrBlankPathPattern_FailsNamingTheOptionAndThePosition(string? pattern)
    {
        var include = new NetApiFlowOptionsValidator().Validate(null, new NetApiFlowOptions().IncludePaths("/api/**", pattern!));
        var exclude = new NetApiFlowOptionsValidator().Validate(null, new NetApiFlowOptions().ExcludePaths("/health/**", pattern!));

        include.Failed.ShouldBeTrue();
        include.FailureMessage.ShouldContain("IncludePaths");
        include.FailureMessage.ShouldContain("position 1");
        exclude.Failed.ShouldBeTrue();
        exclude.FailureMessage.ShouldContain("ExcludePaths");
        exclude.FailureMessage.ShouldContain("position 1");
    }

    [Fact]
    public void Validate_WithThePatternsThatWereAlreadyValid_Succeeds()
    {
        var options = new NetApiFlowOptions()
            .IncludePaths("/api/**", "/orders/*", "/", "**")
            .ExcludePaths("/health", "/api/internal/**");

        new NetApiFlowOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();
    }
}
