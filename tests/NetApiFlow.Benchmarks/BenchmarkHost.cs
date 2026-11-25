using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetApiFlow.Domain;

namespace NetApiFlow.Benchmarks;

internal static class BenchmarkHost
{
    public const int StepsPerRequest = 250;

    public static TestServer Start(Action<NetApiFlowOptions>? configureNetApiFlow, string pathBase)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    if (configureNetApiFlow is not null)
                    {
                        services.AddNetApiFlow(configureNetApiFlow);
                    }
                });
                webBuilder.Configure(app =>
                {
                    if (pathBase.Length > 0)
                    {
                        app.UsePathBase(pathBase);
                    }

                    app.UseRouting();
                    app.UseEndpoints(MapEndpoints);
                });
            })
            .Start();

        return host.GetTestServer();
    }

    private static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/simple", () => Results.Ok());
        endpoints.MapGet("/excluded", () => Results.Ok());
        endpoints.MapGet("/nested", async () =>
        {
            using var controller = Flow.Step("OrdersController", "Get", ComponentType.Controller);
            using var service = Flow.Step("OrderService", "Process", ComponentType.Service);
            using var repository = Flow.Step("OrderRepository", "FindAsync", ComponentType.Repository);
            using var database = Flow.Step("Database", "SELECT orders", ComponentType.Database);
            await Task.Yield();
            return Results.Ok();
        });
        endpoints.MapGet("/steps", () =>
        {
            RunSequentialSteps();
            return Results.Ok();
        });
    }

    public static void RunSequentialSteps()
    {
        for (var step = 0; step < StepsPerRequest; step++)
        {
            using var openStep = Flow.Step("OrderService", "Process", ComponentType.Service);
        }
    }
}
