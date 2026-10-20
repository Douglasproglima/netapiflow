namespace SampleApi.MinimalMediatR.Data;

public interface IOrderRepository
{
    Task<Order?> FindAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> FindManyAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken);
}
