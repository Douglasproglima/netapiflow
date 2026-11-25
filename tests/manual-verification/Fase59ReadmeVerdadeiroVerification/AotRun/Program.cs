using NetApiFlow;
using NetApiFlow.Marking;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddNetApiFlow();
builder.Services.AddFlowTracked<IOrderRepository, OrderRepository>(ComponentType.Repository);

var app = builder.Build();
app.MapGet("/tracked", (IOrderRepository repository) => repository.Find(1));
app.MapGet("/manual", () =>
{
    using var step = Flow.Step("Manual", "Step");
    return "ok";
});
app.MapGet("/plain", () => "plain");
app.Run();

public interface IOrderRepository
{
    string Find(int id);
}

public sealed class OrderRepository : IOrderRepository
{
    public string Find(int id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
