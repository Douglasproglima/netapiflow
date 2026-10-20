using Microsoft.EntityFrameworkCore;
using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.EntityFrameworkCore;
using NetApiFlow.Marking;
using SampleApi.Controllers.Application;
using SampleApi.Controllers.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddNetApiFlow(o => o.IncludePaths("/api/**"));
builder.Services.AddNetApiFlowDashboard();
builder.Services.AddNetApiFlowEntityFrameworkCore();

builder.Services.AddDbContext<SampleDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"));
    options.AddInterceptors(sp.GetRequiredService<FlowDbCommandInterceptor>());
});

builder.Services.AddFlowTracked<IOrderRepository, OrderRepository>(ComponentType.Repository);
builder.Services.AddFlowTracked<IOrderService, OrderService>(ComponentType.Service);

var app = builder.Build();

app.UseExceptionHandler(exceptionApp => exceptionApp.Run(
    context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return Task.CompletedTask;
    }));

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    await dbContext.Database.EnsureCreatedAsync();

    if (!await dbContext.Orders.AnyAsync())
    {
        dbContext.Orders.AddRange(
            new Order { Id = 1, CustomerName = "Ada Lovelace", Amount = 120.50m, Status = "Pending" },
            new Order { Id = 2, CustomerName = "Alan Turing", Amount = 75.00m, Status = "Shipped" },
            new Order { Id = 3, CustomerName = "Grace Hopper", Amount = 42.00m, Status = "Pending" });
        await dbContext.SaveChangesAsync();
    }
}

app.MapControllers();
app.UseNetApiFlowDashboard();

app.Run();
