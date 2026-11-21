using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class TraceIdHeaderIntegrationTests
{
    private const string TraceIdHeader = "X-NetApiFlow-TraceId";
    private const string OkRoute = "/api/ok";
    private const string FailingRoute = "/api/fails";

    [Fact]
    public async Task Request_ByDefaultInProduction_CarriesTheTraceIdHeader()
    {
        using var host = await StartHostAsync();

        var response = await host.GetTestServer().CreateClient().GetAsync(OkRoute);

        response.Headers.TryGetValues(TraceIdHeader, out var values).ShouldBeTrue();
        values!.Single().Length.ShouldBe(32);
    }

    [Fact]
    public async Task Request_WithEmitTraceIdHeaderOff_CarriesNoHeaderButTheTraceIsStillRecorded()
    {
        using var host = await StartHostAsync(options => options.EmitTraceIdHeader = false);

        var response = await host.GetTestServer().CreateClient().GetAsync(OkRoute);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains(TraceIdHeader).ShouldBeFalse();
        (await WaitForRecordedRoutesAsync(host)).ShouldBe([OkRoute]);
    }

    [Fact]
    public async Task Request_WithEmitTraceIdHeaderOffOnlyInConfiguration_CarriesNoHeader()
    {
        using var host = await StartHostAsync(
            configuration: new Dictionary<string, string?> { ["NetApiFlow:EmitTraceIdHeader"] = "false" });

        var response = await host.GetTestServer().CreateClient().GetAsync(OkRoute);

        response.Headers.Contains(TraceIdHeader).ShouldBeFalse();
        (await WaitForRecordedRoutesAsync(host)).ShouldBe([OkRoute]);
    }

    [Fact]
    public async Task Request_HandledByAnExceptionHandlerThatClearsTheResponse_ByDefaultStillCarriesTheHeader()
    {
        using var host = await StartHostAsync(useExceptionHandler: true);

        var response = await host.GetTestServer().CreateClient().GetAsync(FailingRoute);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.Contains(TraceIdHeader).ShouldBeTrue();
    }

    [Fact]
    public async Task Request_HandledByAnExceptionHandlerThatClearsTheResponse_WithEmitTraceIdHeaderOffCarriesNoHeader()
    {
        using var host = await StartHostAsync(options => options.EmitTraceIdHeader = false, useExceptionHandler: true);

        var response = await host.GetTestServer().CreateClient().GetAsync(FailingRoute);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.Contains(TraceIdHeader).ShouldBeFalse();
    }

    private static async Task<IHost> StartHostAsync(
        Action<NetApiFlowOptions>? configureOptions = null,
        Dictionary<string, string?>? configuration = null,
        bool useExceptionHandler = false)
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Production);
                if (configuration is not null)
                {
                    webBuilder.ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(configuration));
                }

                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow(configureOptions);
                });
                webBuilder.Configure(app =>
                {
                    if (useExceptionHandler)
                    {
                        app.UseExceptionHandler(errorApp => errorApp.Run(context =>
                        {
                            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                            return Task.CompletedTask;
                        }));
                    }

                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet(OkRoute, () => Results.Ok());
                        endpoints.MapGet(FailingRoute, () => Results.Ok(Throw()));
                    });
                });
            })
            .StartAsync();
    }

    private static string Throw() => throw new InvalidOperationException("boom");

    private static async Task<IReadOnlyList<string>> WaitForRecordedRoutesAsync(IHost host)
    {
        var store = host.Services.GetRequiredService<ITraceStore>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            var recorded = await store.QueryAsync(new TraceQuery(), CancellationToken.None);
            if (recorded.Items.Count > 0 || DateTime.UtcNow >= deadline)
            {
                return recorded.Items.Select(trace => trace.RouteTemplate).ToList();
            }

            await Task.Delay(20);
        }
    }
}
