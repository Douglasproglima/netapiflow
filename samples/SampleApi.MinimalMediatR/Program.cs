using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.EntityFrameworkCore;
using NetApiFlow.Marking;
using NetApiFlow.MediatR;
using SampleApi.MinimalMediatR.Application;
using SampleApi.MinimalMediatR.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNetApiFlow(o => o.IncludePaths("/api/**"));
builder.Services.AddNetApiFlowDashboard();
builder.Services.AddNetApiFlowEntityFrameworkCore();

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
builder.Services.AddNetApiFlowMediatR();

// Kept open for the lifetime of the host: SQLite's :memory: database only exists while a
// connection to it stays open, and EF Core's InMemory provider never calls DbCommandInterceptor.
builder.Services.AddSingleton(_ =>
{
    var connection = new SqliteConnection("Data Source=:memory:");
    connection.Open();
    return connection;
});

builder.Services.AddDbContext<SampleDbContext>((sp, options) =>
{
    options.UseSqlite(sp.GetRequiredService<SqliteConnection>());
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

var orders = app.MapGroup("/api/orders").WithNetApiFlowTracking();

orders.MapGet("/{id:int}", async (int id, IMediator mediator, CancellationToken cancellationToken) =>
{
    var order = await mediator.Send(new GetOrderQuery(id), cancellationToken);
    return order is null ? Results.NotFound() : Results.Ok(order);
});

orders.MapGet("/{id:int}/confirm", async (int id, IMediator mediator, CancellationToken cancellationToken) =>
    Results.Ok(await mediator.Send(new ConfirmOrderCommand(id), cancellationToken)));

orders.MapGet("/{id:int}/slow", async (int id, IMediator mediator, CancellationToken cancellationToken) =>
{
    var order = await mediator.Send(new GetOrderSlowlyQuery(id), cancellationToken);
    return order is null ? Results.NotFound() : Results.Ok(order);
});

orders.MapGet("/parallel", async (int[] ids, IMediator mediator, CancellationToken cancellationToken) =>
    Results.Ok(await mediator.Send(new GetOrdersParallelQuery(ids), cancellationToken)));

app.UseNetApiFlowDashboard();

app.Run();
