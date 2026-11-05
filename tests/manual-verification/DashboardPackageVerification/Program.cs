using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNetApiFlow();
builder.Services.AddNetApiFlowDashboard();

var app = builder.Build();

// Both mounted in the same run: the first proves the default path, the second proves
// an arbitrary one. Open either in a browser, or let DashboardPackageVerification.Tests check both
// with HttpClient.
app.UseNetApiFlowDashboard("/flow");
app.UseNetApiFlowDashboard("/qualquer/caminho");

app.Run();

/// <summary>Exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in DashboardPackageVerification.Tests.</summary>
public partial class Program;
