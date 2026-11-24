using NetApiFlow;
using static NetApiFlow.Flow;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddNetApiFlow();

var app = builder.Build();

app.MapGet("/orders/{id}", (int id) =>
{
    using var step = Step("OrderRepository", "FindAsync", ComponentType.Repository);
    return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
});

app.Run();
