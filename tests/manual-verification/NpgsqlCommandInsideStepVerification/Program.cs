using Microsoft.EntityFrameworkCore;
using Npgsql;
using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.EntityFrameworkCore;
using NetApiFlow.Storage.PostgreSql;
using Flow = NetApiFlow.NetApiFlow;

var connectionString = Environment.GetEnvironmentVariable("SMOKE_PG")!;

var builder = WebApplication.CreateBuilder(args);

if (Environment.GetEnvironmentVariable("SMOKE_CONTROL") == "1")
{
    builder.Configuration.AddJsonFile("appsettings.control.json");
}

builder.Services.AddControllers();
builder.Services.AddNetApiFlow();
builder.Services.AddNetApiFlowDashboard();
builder.Services.AddNetApiFlowPostgreSqlStorage(connectionString);
builder.Services.AddNetApiFlowEntityFrameworkCore();
builder.Services.AddDbContext<SmokeDbContext>((serviceProvider, options) => options
    .UseNpgsql(connectionString)
    .AddInterceptors(serviceProvider.GetRequiredService<FlowDbCommandInterceptor>()));

var app = builder.Build();
app.UseNetApiFlowDashboard();
app.MapControllers();
app.MapGet("/mini/step", async () =>
{
    using var step = Flow.Step("MiniEndpoint", "Count");
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("SELECT 42", connection);
    return await command.ExecuteScalarAsync();
});
app.Run();

public sealed class SmokeDbContext(DbContextOptions<SmokeDbContext> options) : DbContext(options);
