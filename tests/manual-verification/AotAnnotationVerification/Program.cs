using NetApiFlow;
using NetApiFlow.Marking;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddNetApiFlow();

builder.Services.AddFlowTracked<IOrderRepository, OrderRepository>(ComponentType.Repository);
builder.Services.DecorateFlowTracked(type => type.Name.EndsWith("Repository", StringComparison.Ordinal));

builder.Build().Run();

public interface IOrderRepository
{
    string Find(int id);
}

public sealed class OrderRepository : IOrderRepository
{
    public string Find(int id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
