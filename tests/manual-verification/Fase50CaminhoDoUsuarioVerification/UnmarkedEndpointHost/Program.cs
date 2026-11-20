using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNetApiFlow();
builder.Services.AddNetApiFlowDashboard();

var app = builder.Build();
app.UseNetApiFlowDashboard();

app.MapGet("/plain", () => "plain");
app.MapGet("/tracked", () => "tracked").WithNetApiFlowTracking();

app.Run();
