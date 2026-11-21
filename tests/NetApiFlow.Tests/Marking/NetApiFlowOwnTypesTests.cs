using System.Xml.Linq;
using NetApiFlow.Marking;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Marking;

public sealed class NetApiFlowOwnTypesTests
{
    [Fact]
    public void ThePackageAssemblyList_IsExactlyTheSetOfPackageIdsOfTheRepository()
    {
        var packageIds = PackageIdsOfTheSourceProjects();

        packageIds.ShouldNotBeEmpty();
        NetApiFlowOwnTypes.PackageAssemblyNames.ShouldBe(packageIds, ignoreOrder: true);
    }

    private static List<string> PackageIdsOfTheSourceProjects()
    {
        var sourceFolder = Path.Combine(FindRepositoryRoot(), "src");
        return Directory.GetDirectories(sourceFolder)
            .SelectMany(project => Directory.GetFiles(project, "*.csproj", SearchOption.TopDirectoryOnly))
            .SelectMany(projectFile => XDocument.Load(projectFile).Descendants("PackageId").Select(element => element.Value.Trim()))
            .ToList();
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
