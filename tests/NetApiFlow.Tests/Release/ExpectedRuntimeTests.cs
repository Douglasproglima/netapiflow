using System.Globalization;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Release;

public class ExpectedRuntimeTests
{
    private const string ExpectedMajorVariable = "NETAPIFLOW_EXPECT_RUNTIME_MAJOR";

    [Fact]
    public void Runtime_WhenTheCiNamesTheMajorTheSuiteMustRunOn_IsThatMajor()
    {
        var expected = Environment.GetEnvironmentVariable(ExpectedMajorVariable);
        if (string.IsNullOrEmpty(expected))
        {
            return;
        }

        Environment.Version.Major.ShouldBe(int.Parse(expected, CultureInfo.InvariantCulture));
    }
}
