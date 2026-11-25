using Microsoft.Data.SqlClient;
using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNetApiFlow(options => options.ExternalSources = ExternalActivitySource.SqlClient);
builder.Services.AddNetApiFlowDashboard();

var app = builder.Build();
app.MapNetApiFlowDashboard();
app.MapGet("/sql", async () =>
{
    await using var connection = new SqlConnection(Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING"));
    await connection.OpenAsync();
    await using var command = new SqlCommand("SELECT 1", connection);
    var value = await command.ExecuteScalarAsync();
    return $"{typeof(SqlConnection).Assembly.GetName().Version} -> {value}";
});
app.Run();
