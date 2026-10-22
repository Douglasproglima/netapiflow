using Microsoft.Extensions.DependencyInjection;
using SampleApi.MinimalMediatR.Data;

namespace SampleApi.MinimalMediatR.Application;

public sealed class OrderService(IOrderRepository repository, IServiceScopeFactory scopeFactory) : IOrderService
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
        // EF Core's DbContext isn't thread-safe; each parallel branch needs its own DI scope
        // (and therefore its own DbContext) to query concurrently without throwing.
        var tasks = ids.Select(async id =>
        {
            using var scope = scopeFactory.CreateScope();
            var scopedRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
            return await scopedRepository.FindAsync(id, cancellationToken);
        });
        var results = await Task.WhenAll(tasks);
        return [.. results.Where(o => o is not null).Select(o => o!)];
    }
}
