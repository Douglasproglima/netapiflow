using NetApiFlow.Dashboard.StaticFiles;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

/// <summary>Verifies the Angular client's dist/ is embedded and resolvable (tarefa 5.2, ADR-0005).</summary>
public class DashboardEmbeddedFilesTests
{
    [Fact]
    public void CreateFileProvider_ResolvesIndexHtmlFromTheCompiledAssembly()
    {
        var provider = DashboardEmbeddedFiles.CreateFileProvider();

        var file = provider.GetFileInfo("index.html");

        file.Exists.ShouldBeTrue();
        using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream);
        reader.ReadToEnd().ShouldContain("<app-root>");
    }
}
