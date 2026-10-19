using SampleApi.MinimalMediatR.Data;

namespace SampleApi.MinimalMediatR.Application;

public sealed class OrderService(IOrderRepository repository) : IOrderService
{
    public Task<Order?> GetAsync(int id, CancellationToken cancellationToken) =>
        repository.FindAsync(id, cancellationToken);

    public async Task<Order> GetWithBusinessRuleAsync(int id, CancellationToken cancellationToken)
    {
        var order = await repository.FindAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Order {id} does not exist.");

        if (order.Status != "Pending")
        {
            throw new InvalidOperationException($"Order {id} is not Pending (current status: {order.Status}).");
        }

        return order;
    }

    public async Task<Order?> GetSlowlyAsync(int id, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
        return await repository.FindAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetManyInParallelAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken)
    {
        var tasks = ids.Select(id => repository.FindAsync(id, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return [.. results.Where(o => o is not null).Select(o => o!)];
    }
}
