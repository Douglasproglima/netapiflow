using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetApiFlow.EntityFrameworkCore.Ef8Tests;

internal sealed class EfCore8Host : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly IHost _host;

    public EfCore8Host()
    {
        _connection.Open();
        _host = CreateHost();
        CreateSchema();
        Client = _host.GetTestServer().CreateClient();
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => _host.Services;

    public void Dispose()
    {
        Client.Dispose();
        _host.Dispose();
        _connection.Dispose();
    }

    private IHost CreateHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(ConfigureServices);
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(MapEndpoints);
                });
            })
            .Start();

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddRouting();
        services.AddNetApiFlow(options => options.IncludePaths("/**"));
        services.AddNetApiFlowEntityFrameworkCore();
        services.AddDbContext<SingleAttachedDbContext>((serviceProvider, options) => options
            .UseSqlite(_connection)
            .AddInterceptors(serviceProvider.GetRequiredService<FlowDbCommandInterceptor>()));
        services.AddDbContext<DoubleAttachedDbContext>((serviceProvider, options) =>
        {
            var interceptor = serviceProvider.GetRequiredService<FlowDbCommandInterceptor>();
            options.UseSqlite(_connection).AddInterceptors(interceptor).AddInterceptors(interceptor);
        });
    }

    private static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/query", async (SingleAttachedDbContext context) =>
            Results.Ok(await context.Widgets.CountAsync()));

        endpoints.MapPost("/insert", async (SingleAttachedDbContext context) =>
        {
            context.Widgets.Add(new Widget { Name = "floor" });
            await context.SaveChangesAsync();
            return Results.Ok();
        });

        endpoints.MapGet("/query-double-attached", async (DoubleAttachedDbContext context) =>
            Results.Ok(await context.Widgets.CountAsync()));
    }

    private void CreateSchema()
    {
        using var scope = _host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SingleAttachedDbContext>().Database.EnsureCreated();
    }
}
