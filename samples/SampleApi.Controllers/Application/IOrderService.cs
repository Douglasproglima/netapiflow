using SampleApi.Controllers.Data;

namespace SampleApi.Controllers.Application;

public interface IOrderService
{
    Task<Order?> GetAsync(int id, CancellationToken cancellationToken);

    Task<Order> GetWithBusinessRuleAsync(int id, CancellationToken cancellationToken);

    Task<Order?> GetSlowlyAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetManyInParallelAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken);

    Task<string> GetShippingStatusAsync(int id, CancellationToken cancellationToken);
}
