using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc.Filters;
using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.Storage.SqlServer;

var connectionString = Environment.GetEnvironmentVariable("SMOKE_SQL")!;

var builder = WebApplication.CreateBuilder(args);

if (Environment.GetEnvironmentVariable("SMOKE_CONTROL") == "1")
{
    builder.Configuration.Sources.Clear();
    builder.Configuration.AddJsonFile("appsettings.control.json");
    builder.Configuration.AddEnvironmentVariables();
}

builder.Services.AddControllers(options => options.Filters.Add(new AllowAllAuthorizationFilter()));
builder.Services.AddNetApiFlow();
builder.Services.AddNetApiFlowDashboard();
builder.Services.AddNetApiFlowSqlServerStorage(connectionString);

var app = builder.Build();
app.UseNetApiFlowDashboard();
app.MapControllers();
app.MapGet("/mini/plain", async () =>
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new SqlCommand("SELECT 42", connection);
    return await command.ExecuteScalarAsync();
});
app.Run();

internal sealed class AllowAllAuthorizationFilter : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context) => Task.CompletedTask;
}
