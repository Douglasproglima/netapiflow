using MediatR;
using SampleApi.MinimalMediatR.Data;

namespace SampleApi.MinimalMediatR.Application;

/// <summary>Success scenario: fetches an existing order.</summary>
public sealed record GetOrderQuery(int Id) : IRequest<Order?>;

public sealed class GetOrderQueryHandler(IOrderService orderService) : IRequestHandler<GetOrderQuery, Order?>
{
    public Task<Order?> Handle(GetOrderQuery request, CancellationToken cancellationToken) =>
        orderService.GetAsync(request.Id, cancellationToken);
}
