using MediatR;
using SampleApi.MinimalMediatR.Data;

namespace SampleApi.MinimalMediatR.Application;

/// <summary>Sequential scenario: demonstrates the N+1 anti-pattern by querying each order one by one in the same request.</summary>
public sealed record GetOrdersSequentiallyQuery(IReadOnlyList<int> Ids) : IRequest<IReadOnlyList<Order>>;

public sealed class GetOrdersSequentiallyQueryHandler(IOrderService orderService)
    : IRequestHandler<GetOrdersSequentiallyQuery, IReadOnlyList<Order>>
{
    public Task<IReadOnlyList<Order>> Handle(GetOrdersSequentiallyQuery request, CancellationToken cancellationToken) =>
        orderService.GetManySequentiallyAsync(request.Ids, cancellationToken);
}
