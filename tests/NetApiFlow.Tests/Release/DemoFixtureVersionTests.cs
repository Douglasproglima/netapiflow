using System.Text.Json;
using System.Xml.Linq;
using Shouldly;

namespace NetApiFlow.Tests.Release;

public sealed class DemoFixtureVersionTests
{
    [Fact]
    public void DemoConfigFixture_ShowsTheVersionDeclaredInDirectoryBuildProps()
    {
        var repositoryRoot = FindRepositoryRoot();

        var declaredVersion = XDocument
            .Load(Path.Combine(repositoryRoot, "Directory.Build.props"))
            .Descendants("Version")
            .Single()
            .Value;

        var fixturePath = Path.Combine(
            repositoryRoot, "src", "NetApiFlow.Dashboard", "ClientApp", "src", "assets", "demo-fixtures", "config.json");
        using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var fixtureVersion = fixture.RootElement.GetProperty("version").GetString();

        fixtureVersion.ShouldBe(declaredVersion);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NetApiFlow.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("NetApiFlow.slnx was not found above the test output folder.");
    }
}
