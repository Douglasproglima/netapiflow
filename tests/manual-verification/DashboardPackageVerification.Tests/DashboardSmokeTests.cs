using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace DashboardPackageVerification.Tests;

/// <summary>
/// Proves the published .nupkg works standalone: the dashboard opens at its default
/// path and at an arbitrary one, every asset the rewritten HTML references resolves, and nothing
/// relies on being built from source inside this repo's own solution.
/// </summary>
public class DashboardSmokeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/flow", "/flow/")]
    [InlineData("/qualquer/caminho", "/qualquer/caminho/")]
    public async Task GetDashboardRoot_ReturnsHtmlWithTheRightBaseHref(string path, string expectedBaseHref)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain($"<base href=\"{expectedBaseHref}\">");
    }

    [Theory]
    [InlineData("/flow")]
    [InlineData("/qualquer/caminho")]
    public async Task GetEveryAssetReferencedByTheIndexHtml_Resolves(string dashboardPath)
    {
        var client = factory.CreateClient();
        var html = await (await client.GetAsync(dashboardPath)).Content.ReadAsStringAsync();

        foreach (var assetPath in ExtractAssetPaths(html))
        {
            var response = await client.GetAsync($"{dashboardPath}/{assetPath}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"asset '{assetPath}' under '{dashboardPath}' must not 404");
        }
    }

    private static IEnumerable<string> ExtractAssetPaths(string html)
    {
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
            html, "(?:href|src)=\"([^\"/][^\"]*\\.(?:js|css|ico))\""))
        {
            yield return match.Groups[1].Value;
        }
    }
}
