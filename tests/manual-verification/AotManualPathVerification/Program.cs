using Microsoft.EntityFrameworkCore;
using NetApiFlow;
using NetApiFlow.EntityFrameworkCore;
using static NetApiFlow.Flow;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddNetApiFlow();
builder.Services.AddNetApiFlowEntityFrameworkCore();
builder.Services.AddDbContext<OrderContext>((serviceProvider, options) =>
{
    options.UseSqlite("Data Source=orders.db");
    options.AddInterceptors(serviceProvider.GetRequiredService<FlowDbCommandInterceptor>());
});

var app = builder.Build();

app.MapGet("/orders/{id}", (int id, OrderContext context) =>
{
    using var step = Step("OrderRepository", "FindAsync", ComponentType.Repository);
    return context.Orders.FirstOrDefault(order => order.Id == id)?.Reference;
});

app.Run();

public sealed class Order
{
    public int Id { get; set; }

    public string Reference { get; set; } = string.Empty;
}

public sealed class OrderContext(DbContextOptions<OrderContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}
