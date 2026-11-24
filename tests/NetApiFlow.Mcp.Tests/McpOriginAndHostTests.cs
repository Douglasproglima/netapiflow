using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Mcp;
using NetApiFlow.Mcp.Endpoints;
using NetApiFlow.TestSupport;
using Shouldly;
using Xunit;

namespace NetApiFlow.Mcp.Tests;

public class McpOriginAndHostTests
{
    private const string InitializeBody =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""";

    [Theory]
    [MemberData(nameof(OriginAndHostCases.Names), MemberType = typeof(OriginAndHostCases))]
    public async Task Initialize_AppliesTheSharedOriginAndHostTable(string caseName)
    {
        var testCase = OriginAndHostCases.ByName(caseName);
        using var host = await StartHostAsync(testCase);
        var client = host.GetTestServer().CreateClient();

        using var response = await SendAsync(client, HttpMethod.Post, testCase, InitializeBody);

        await AssertOutcomeAsync(response, testCase, "POST initialize");
    }

    [Theory]
    [InlineData("dev rebinding, foreign host without origin")]
    [InlineData("dev localhost, foreign origin")]
    [InlineData("prod foreign origin")]
    public async Task GetAndDelete_AreNeverServedWhenTheSharedTableRefusesThem(string caseName)
    {
        var testCase = OriginAndHostCases.ByName(caseName);
        using var host = await StartHostAsync(testCase);
        var client = host.GetTestServer().CreateClient();

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Delete })
        {
            using var response = await SendAsync(client, method, testCase, body: null);

            ((int)response.StatusCode).ShouldBeOneOf(
                [StatusCodes.Status403Forbidden, StatusCodes.Status405MethodNotAllowed],
                $"{method} /flow/mcp answered {(int)response.StatusCode}");
        }
    }

    private static async Task AssertOutcomeAsync(HttpResponseMessage response, OriginAndHostCase testCase, string request)
    {
        var refused = response.StatusCode == HttpStatusCode.Forbidden;
        var body = refused ? await response.Content.ReadAsStringAsync() : string.Empty;

        if (testCase.RefusedBecause is null)
        {
            refused.ShouldBeFalse($"{request} was refused: {body}");
            return;
        }

        refused.ShouldBeTrue($"{request} answered {(int)response.StatusCode} instead of 403");
        body.ShouldContain(OriginAndHostCases.MessageFragmentFor(testCase.RefusedBecause));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, OriginAndHostCase testCase, string? body)
    {
        var request = new HttpRequestMessage(method, "/flow/mcp");
        request.Headers.Host = testCase.Host;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (testCase.Origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", testCase.Origin);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    private static async Task<IHost> StartHostAsync(OriginAndHostCase testCase)
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(configuration =>
            {
                if (testCase.AllowedHosts is not null)
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = testCase.AllowedHosts });
                }
            })
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(testCase.Environment);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow();
                    services.AddNetApiFlowMcp();
                    if (testCase.Environment == OriginAndHostCases.Production)
                    {
                        services.AddSingleton<IFlowAccessFilter>(new AllowingAccessFilter());
                    }
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapNetApiFlowMcp());
                });
            })
            .StartAsync();
    }

    private sealed class AllowingAccessFilter : IFlowAccessFilter
    {
        public ValueTask<bool> AuthorizeAsync(HttpContext context) => new(true);
    }
}
