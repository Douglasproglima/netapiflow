using MediatR;
using SampleApi.MinimalMediatR.Data;

namespace SampleApi.MinimalMediatR.Application;

/// <summary>Parallel scenario: fetches several orders concurrently via <see cref="Task.WhenAll"/>.</summary>
public sealed record GetOrdersParallelQuery(IReadOnlyList<int> Ids) : IRequest<IReadOnlyList<Order>>;

public sealed class GetOrdersParallelQueryHandler(IOrderService orderService)
    : IRequestHandler<GetOrdersParallelQuery, IReadOnlyList<Order>>
{
    public Task<IReadOnlyList<Order>> Handle(GetOrdersParallelQuery request, CancellationToken cancellationToken) =>
        orderService.GetManyInParallelAsync(request.Ids, cancellationToken);
}
