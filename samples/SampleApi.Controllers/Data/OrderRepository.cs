using Microsoft.EntityFrameworkCore;

namespace SampleApi.Controllers.Data;

public sealed class OrderRepository(SampleDbContext dbContext) : IOrderRepository
{
    public Task<Order?> FindAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Order>> FindManyAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken)
    {
        var results = new List<Order>();
        foreach (var id in ids)
        {
            var order = await dbContext.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
            if (order is not null)
            {
                results.Add(order);
            }
        }

        return results;
    }
}
