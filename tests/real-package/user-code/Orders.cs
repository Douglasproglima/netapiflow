public interface IOrderRepository
{
    Task<string> FindAsync(int id);
}

public sealed class OrderRepository : IOrderRepository
{
    public async Task<string> FindAsync(int id)
    {
        await Task.Delay(1);
        return $"order-{id}";
    }
}

public interface IOrderService
{
    Task<string> GetAsync(int id);
}

public sealed class OrderService(IOrderRepository repository) : IOrderService
{
    public Task<string> GetAsync(int id) => repository.FindAsync(id);
}
