using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.Dashboard.StaticFiles;
using NetApiFlow.TestSupport;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class DashboardSecurityHeadersTests
{
    [Theory]
    [InlineData("GET", "/flow/api/traces")]
    [InlineData("DELETE", "/flow/api/traces")]
    [InlineData("GET", "/flow/api/stream")]
    [InlineData("GET", "/flow/")]
    [InlineData("GET", "/flow/history")]
    public async Task EveryDashboardRoute_ComesWithTheSecurityHeaders(string method, string path)
    {
        using var host = await StartHostAsync(Environments.Development);
        using var response = await SendAsync(host, new HttpMethod(method), path);

        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task AStaticFile_ComesWithTheSecurityHeaders()
    {
        var script = Regex.Match(ReadEmbeddedIndexHtml(), @"src=""([^""]+\.js)""").Groups[1].Value;
        script.ShouldNotBeNullOrEmpty();
        using var host = await StartHostAsync(Environments.Development);
        using var response = await SendAsync(host, HttpMethod.Get, $"/flow/{script}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task ARefusedRequest_ComesWithTheSecurityHeaders()
    {
        using var host = await StartHostAsync(Environments.Development, denyEveryone: true);
        using var response = await SendAsync(host, HttpMethod.Get, "/flow/api/traces");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        AssertSecurityHeaders(response);
    }

    [Theory]
    [InlineData("evil.example", null)]
    [InlineData("localhost", "http://evil.example")]
    public async Task ARequestRefusedByTheHostAndOriginGuard_ComesWithTheSecurityHeaders(string host, string? origin)
    {
        using var server = await StartHostAsync(Environments.Development);
        var request = new HttpRequestMessage(HttpMethod.Get, "/flow/api/traces");
        request.Headers.Host = host;
        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        using var response = await server.GetTestServer().CreateClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain("NetApiFlow refused this request");
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task ARouteThatAnswers404OutsideDevelopment_ComesWithTheSecurityHeaders()
    {
        using var host = await StartHostAsync(Environments.Production);
        using var response = await SendAsync(host, HttpMethod.Get, "/flow/api/traces");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        AssertSecurityHeaders(response);
    }

    [Theory]
    [InlineData("/flow/")]
    [InlineData("/flow/history")]
    [InlineData("/flow/anything/not/a/file")]
    public async Task TheDashboardDocument_CarriesTheWholePolicy(string path)
    {
        using var host = await StartHostAsync(Environments.Development);
        using var response = await SendAsync(host, HttpMethod.Get, path);

        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldBe(ExpectedSecurityHeaders.DocumentPolicy);
    }

    [Fact]
    public async Task TheEmbeddedIndexHtml_HasNoInlineScriptNoInlineStyleNoEventHandlerAndNoStyleAttribute()
    {
        var html = ReadEmbeddedIndexHtml();

        Regex.Count(html, @"<script(?![^>]*\ssrc=)[^>]*>").ShouldBe(0, "an inline <script> needs 'unsafe-inline' or a hash in script-src");
        Regex.Count(html, @"<style\b").ShouldBe(0, "an inline <style> needs 'unsafe-inline' in style-src for the document");
        Regex.Count(html, @"\son[a-z]+\s*=").ShouldBe(0, "an on* attribute is an inline script");
        Regex.Count(html, @"\sstyle\s*=").ShouldBe(0, "a style attribute needs 'unsafe-inline'");
    }

    private static string ReadEmbeddedIndexHtml()
    {
        using var stream = DashboardEmbeddedFiles.CreateFileProvider().GetFileInfo("index.html").CreateReadStream();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        foreach (var (name, value) in ExpectedSecurityHeaders.OnEveryResponse)
        {
            response.Headers.TryGetValues(name, out var values).ShouldBeTrue($"{name} is missing");
            values!.Single().ShouldBe(value);
        }

        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain(ExpectedSecurityHeaders.FrameAncestors);
    }

    private static Task<HttpResponseMessage> SendAsync(IHost host, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        return host.GetTestServer().CreateClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    private static async Task<IHost> StartHostAsync(string environment, bool denyEveryone = false)
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(environment);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow();
                    services.AddNetApiFlowDashboard();
                    if (denyEveryone)
                    {
                        services.AddSingleton<IFlowAccessFilter>(new DenyingAccessFilter());
                    }
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapNetApiFlowDashboard());
                });
            })
            .StartAsync();
    }

    private sealed class DenyingAccessFilter : IFlowAccessFilter
    {
        public ValueTask<bool> AuthorizeAsync(HttpContext context) => new(false);
    }
}
