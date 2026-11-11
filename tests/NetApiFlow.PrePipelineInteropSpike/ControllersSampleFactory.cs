using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetApiFlow.EntityFrameworkCore;
using SampleApi.Controllers.Controllers;
using SampleApi.Controllers.Data;

namespace NetApiFlow.PrePipelineInteropSpike;

internal class ControllersSampleFactory : WebApplicationFactory<OrdersController>
{
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAliveConnection;

    public ControllersSampleFactory()
    {
        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<SampleDbContext>>();
            services.RemoveAll<SampleDbContext>();

            services.AddScoped(sp =>
            {
                var options = new DbContextOptionsBuilder<SampleDbContext>()
                    .UseSqlite(_connectionString)
                    .AddInterceptors(sp.GetRequiredService<FlowDbCommandInterceptor>())
                    .Options;
                return new SampleDbContext(options);
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _keepAliveConnection.Dispose();
        }
    }
}
