using SampleApi.MinimalMediatR.Data;

namespace SampleApi.MinimalMediatR.Application;

public interface IOrderService
{
    Task<Order?> GetAsync(int id, CancellationToken cancellationToken);

    Task<Order> GetWithBusinessRuleAsync(int id, CancellationToken cancellationToken);

    Task<Order?> GetSlowlyAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetManyInParallelAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetManySequentiallyAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken);
}
