using NetApiFlow;
using static NetApiFlow.Flow;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddNetApiFlow();

var app = builder.Build();

app.MapGet("/orders/{id}", (int id) =>
{
    using var step = Step("OrderRepository", "FindAsync", ComponentType.Repository);
    step.Tag("order.id", id);
    step.Tag("order.total.cents", 1_250L);
    step.Tag("order.discount", 0.1);
    step.Tag("order.paid", true);
    step.Tag("order.lookup", OrderLookup.ById);
    return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
});

app.Run();

internal enum OrderLookup
{
    ById,
    ByCustomer,
}
