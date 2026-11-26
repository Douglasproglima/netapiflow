using Shouldly;

namespace NetApiFlow.OpenTelemetry.Tests;

public class OpenTelemetryArgumentValidationTests
{
    [Fact]
    public void AddNetApiFlowOtlpExport_WithNullServices_ThrowsNamingTheParameter()
    {
        Should.Throw<ArgumentNullException>(() => NetApiFlowOpenTelemetryServiceCollectionExtensions.AddNetApiFlowOtlpExport(null!))
            .ParamName.ShouldBe("services");
    }

    [Fact]
    public void AddNetApiFlowSource_WithANullBuilder_ThrowsNamingTheParameter()
    {
        Should.Throw<ArgumentNullException>(() => NetApiFlowTracerProviderBuilderExtensions.AddNetApiFlowSource(null!))
            .ParamName.ShouldBe("builder");
    }
}
