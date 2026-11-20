using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddNetApiFlow();
builder.Services.AddNetApiFlowDashboard();
builder.Services.AddNetApiFlowEntityFrameworkCore();

var connection = new SqliteConnection("DataSource=:memory:");
connection.Open();
builder.Services.AddDbContext<FloorDbContext>((serviceProvider, options) => options
    .UseSqlite(connection)
    .AddInterceptors(serviceProvider.GetRequiredService<FlowDbCommandInterceptor>()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<FloorDbContext>().Database.EnsureCreated();
}

app.UseNetApiFlowDashboard();
app.MapControllers();
app.Run();

public sealed class FloorDbContext(DbContextOptions<FloorDbContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();
}

public sealed class Widget
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}
