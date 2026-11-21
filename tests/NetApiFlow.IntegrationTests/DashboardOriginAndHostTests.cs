using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.TestSupport;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class DashboardOriginAndHostTests
{
    [Theory]
    [MemberData(nameof(OriginAndHostCases.Names), MemberType = typeof(OriginAndHostCases))]
    public async Task EveryRestMethod_AppliesTheSharedOriginAndHostTable(string caseName)
    {
        var testCase = OriginAndHostCases.ByName(caseName);
        using var host = await StartHostAsync(testCase);
        var client = host.GetTestServer().CreateClient();

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Delete })
        {
            using var response = await SendAsync(client, method, "/flow/api/traces", testCase);

            await AssertOutcomeAsync(response, testCase, $"{method} /flow/api/traces");
        }
    }

    [Theory]
    [MemberData(nameof(OriginAndHostCases.Names), MemberType = typeof(OriginAndHostCases))]
    public async Task TheStaticDashboardFiles_ApplyTheSharedOriginAndHostTable(string caseName)
    {
        var testCase = OriginAndHostCases.ByName(caseName);
        using var host = await StartHostAsync(testCase);
        var client = host.GetTestServer().CreateClient();

        using var response = await SendAsync(client, HttpMethod.Get, "/flow/", testCase);

        await AssertOutcomeAsync(response, testCase, "GET /flow/");
    }

    [Theory]
    [InlineData("dev rebinding, foreign host without origin")]
    [InlineData("dev localhost, foreign origin")]
    [InlineData("prod foreign origin")]
    public async Task TheLiveStream_IsRefusedWhenTheSharedTableRefusesIt(string caseName)
    {
        var testCase = OriginAndHostCases.ByName(caseName);
        using var host = await StartHostAsync(testCase);
        var client = host.GetTestServer().CreateClient();

        using var response = await SendAsync(client, HttpMethod.Get, "/flow/api/stream", testCase);

        await AssertOutcomeAsync(response, testCase, "GET /flow/api/stream");
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

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, OriginAndHostCase testCase)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Host = testCase.Host;
        if (testCase.Origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", testCase.Origin);
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
                    services.AddNetApiFlowDashboard();
                    if (testCase.Environment == OriginAndHostCases.Production)
                    {
                        services.AddSingleton<IFlowAccessFilter>(new AllowingAccessFilter());
                    }
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.UseNetApiFlowDashboard());
                });
            })
            .StartAsync();
    }

    private sealed class AllowingAccessFilter : IFlowAccessFilter
    {
        public ValueTask<bool> AuthorizeAsync(HttpContext context) => new(true);
    }
}
