using Npgsql;
using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.Storage.PostgreSql;
using NetApiFlow.Storage.SqlServer;

var settings = DatabaseSettings.FromEnvironment();
await DatabasePreparation.PrepareAsync(settings);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(settings);
builder.Services.AddControllers();
builder.Services.AddNetApiFlow(options =>
{
    options.CaptureSqlText = true;
    options.EmitTraceIdHeader = true;
    options.ExcludePaths("/ready");
    options.ExternalSources = settings.Kind == "postgres" ? ExternalActivitySource.Npgsql : ExternalActivitySource.SqlClient;
});
builder.Services.AddNetApiFlowDashboard();

if (settings.Kind == "postgres")
{
    builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(settings.ConnectionString));
    builder.Services.AddNetApiFlowPostgreSqlStorage(
        settings.ConnectionString,
        options =>
        {
            options.TablePrefix = settings.TablePrefix;
            options.AutoCreateSchema = settings.AutoCreateSchema;
        });
}
else
{
    builder.Services.AddNetApiFlowSqlServerStorage(
        settings.ConnectionString,
        options =>
        {
            options.TablePrefix = settings.TablePrefix;
            options.AutoCreateSchema = settings.AutoCreateSchema;
        });
}

var app = builder.Build();
app.MapNetApiFlowDashboard();
app.MapControllers();
app.MapGet("/ready", () => "ready");
app.Run();
