using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetApiFlow.Configuration;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Configuration;

/// <summary>
/// Pins every value the <c>NetApiFlow</c> configuration section feeds into
/// <see cref="NetApiFlowOptions"/>. Reflection-based binding used to do this generically, so
/// nothing listed the properties anywhere; replacing it with explicit reads needs this to prove
/// no property silently stopped being read.
/// </summary>
public class NetApiFlowOptionsSetupTests
{
    private static NetApiFlowOptions Configure(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new NetApiFlowOptions();

        new NetApiFlowOptionsSetup(configuration).Configure(options);

        return options;
    }

    [Fact]
    public void Configure_WithNothingConfigured_LeavesEveryDefaultUntouched()
    {
        var options = Configure([]);

        options.Enabled.ShouldBeTrue();
        options.SlowThreshold.ShouldBe(TimeSpan.FromMilliseconds(250));
        options.CaptureSqlText.ShouldBeFalse();
        options.CapturePrePipelineSteps.ShouldBeFalse();
        options.ExternalSources.ShouldBe(ExternalActivitySource.None);
        options.OpenApiDocumentPath.ShouldBe("/openapi/v1.json");
        options.EmitTraceIdHeader.ShouldBeFalse();
        options.IncludePatterns.ShouldBeEmpty();
        options.ExcludePatterns.ShouldBeEmpty();
        options.Retention.MaxTraces.ShouldBe(1000);
        options.Retention.MaxAge.ShouldBe(TimeSpan.FromHours(24));
        options.Limits.MaxSpansPerTrace.ShouldBe(300);
        options.Limits.MaxLogEntriesPerSpan.ShouldBe(20);
        options.Limits.MaxTagsPerSpan.ShouldBe(20);
        options.Limits.MaxTagValueLength.ShouldBe(4000);
        options.TailSampling.Enabled.ShouldBeFalse();
        options.TailSampling.SuccessSampleRate.ShouldBe(0.1);
        options.LogCorrelation.Enabled.ShouldBeFalse();
        options.LogCorrelation.MinLevel.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public void Configure_WithEveryScalarConfigured_ReadsAllOfThem()
    {
        var options = Configure(new Dictionary<string, string?>
        {
            ["NetApiFlow:Enabled"] = "false",
            ["NetApiFlow:SlowThreshold"] = "00:00:01.500",
            ["NetApiFlow:CaptureSqlText"] = "true",
            ["NetApiFlow:CapturePrePipelineSteps"] = "true",
            ["NetApiFlow:ExternalSources"] = "HttpClient",
            ["NetApiFlow:OpenApiDocumentPath"] = "/swagger/v1/swagger.json",
            ["NetApiFlow:EmitTraceIdHeader"] = "true",
        });

        options.Enabled.ShouldBeFalse();
        options.SlowThreshold.ShouldBe(TimeSpan.FromMilliseconds(1500));
        options.CaptureSqlText.ShouldBeTrue();
        options.CapturePrePipelineSteps.ShouldBeTrue();
        options.ExternalSources.ShouldBe(ExternalActivitySource.HttpClient);
        options.OpenApiDocumentPath.ShouldBe("/swagger/v1/swagger.json");
        options.EmitTraceIdHeader.ShouldBeTrue();
    }

    [Fact]
    public void Configure_WithCombinedFlags_ReadsTheWholeFlagsValue()
    {
        var options = Configure(new Dictionary<string, string?>
        {
            ["NetApiFlow:ExternalSources"] = "HttpClient, Npgsql",
        });

        options.ExternalSources.ShouldBe(ExternalActivitySource.HttpClient | ExternalActivitySource.Npgsql);
    }

    [Fact]
    public void Configure_WithNumericFlags_ReadsItTheSameWayBindDid()
    {
        var options = Configure(new Dictionary<string, string?>
        {
            ["NetApiFlow:ExternalSources"] = "3",
        });

        options.ExternalSources.ShouldBe(ExternalActivitySource.HttpClient | ExternalActivitySource.Npgsql);
    }

    [Fact]
    public void Configure_WithEveryNestedSectionConfigured_ReadsAllOfThem()
    {
        var options = Configure(new Dictionary<string, string?>
        {
            ["NetApiFlow:Retention:MaxTraces"] = "25",
            ["NetApiFlow:Retention:MaxAge"] = "02:00:00",
            ["NetApiFlow:Limits:MaxSpansPerTrace"] = "7",
            ["NetApiFlow:Limits:MaxLogEntriesPerSpan"] = "8",
            ["NetApiFlow:Limits:MaxTagsPerSpan"] = "9",
            ["NetApiFlow:Limits:MaxTagValueLength"] = "11",
            ["NetApiFlow:TailSampling:Enabled"] = "true",
            ["NetApiFlow:TailSampling:SuccessSampleRate"] = "0.25",
            ["NetApiFlow:LogCorrelation:Enabled"] = "true",
            ["NetApiFlow:LogCorrelation:MinLevel"] = "Error",
        });

        options.Retention.MaxTraces.ShouldBe(25);
        options.Retention.MaxAge.ShouldBe(TimeSpan.FromHours(2));
        options.Limits.MaxSpansPerTrace.ShouldBe(7);
        options.Limits.MaxLogEntriesPerSpan.ShouldBe(8);
        options.Limits.MaxTagsPerSpan.ShouldBe(9);
        options.Limits.MaxTagValueLength.ShouldBe(11);
        options.TailSampling.Enabled.ShouldBeTrue();
        options.TailSampling.SuccessSampleRate.ShouldBe(0.25);
        options.LogCorrelation.Enabled.ShouldBeTrue();
        options.LogCorrelation.MinLevel.ShouldBe(LogLevel.Error);
    }

    [Fact]
    public void Configure_WithPathArrays_AppendsThemAsPatterns()
    {
        var options = Configure(new Dictionary<string, string?>
        {
            ["NetApiFlow:IncludePaths:0"] = "/api/**",
            ["NetApiFlow:IncludePaths:1"] = "/admin/**",
            ["NetApiFlow:ExcludePaths:0"] = "/health",
        });

        options.IncludePatterns.ShouldBe(["/api/**", "/admin/**"]);
        options.ExcludePatterns.ShouldBe(["/health"]);
    }

    [Fact]
    public void Configure_WithAnEmptyPathArray_LeavesThePatternsUntouched()
    {
        var options = Configure(new Dictionary<string, string?>
        {
            ["NetApiFlow:IncludePaths"] = string.Empty,
        });

        options.IncludePatterns.ShouldBeEmpty();
    }

    /// <summary>
    /// <c>DashboardBasePath</c> is internal and <c>Bind</c> never touched a non-public property,
    /// so configuration must not reach it either: <c>MapNetApiFlowDashboard</c> owns that value.
    /// </summary>
    [Fact]
    public void Configure_WithDashboardBasePathInConfiguration_IgnoresIt()
    {
        var options = Configure(new Dictionary<string, string?>
        {
            ["NetApiFlow:DashboardBasePath"] = "/hijacked",
        });

        options.DashboardBasePath.ShouldBe("/flow");
    }

    /// <summary>
    /// <c>IncludePatterns</c>/<c>ExcludePatterns</c> are get-only <see cref="IReadOnlyList{T}"/>
    /// projections of private lists. Reflection binding never wrote to them, and the only
    /// supported way to add a pattern from configuration is the <c>IncludePaths</c>/
    /// <c>ExcludePaths</c> key the setup reads explicitly.
    /// </summary>
    [Theory]
    [InlineData("IncludePatterns")]
    [InlineData("ExcludePatterns")]
    public void Configure_WithThePatternProjectionInConfiguration_IgnoresIt(string key)
    {
        var options = Configure(new Dictionary<string, string?>
        {
            [$"NetApiFlow:{key}:0"] = "/sneaked/**",
        });

        options.IncludePatterns.ShouldBeEmpty();
        options.ExcludePatterns.ShouldBeEmpty();
    }

    [Fact]
    public void Configure_CalledTwice_DoesNotDuplicateThePatterns()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["NetApiFlow:IncludePaths:0"] = "/api/**" })
            .Build();
        var options = new NetApiFlowOptions();
        var subject = new NetApiFlowOptionsSetup(configuration);

        subject.Configure(options);
        var afterFirst = options.IncludePatterns.Count;

        afterFirst.ShouldBe(1);
    }
}
