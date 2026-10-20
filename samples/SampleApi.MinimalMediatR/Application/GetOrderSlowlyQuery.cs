using MediatR;
using SampleApi.MinimalMediatR.Data;

namespace SampleApi.MinimalMediatR.Application;

/// <summary>Slow scenario: takes longer than the configured SlowThreshold.</summary>
public sealed record GetOrderSlowlyQuery(int Id) : IRequest<Order?>;

public sealed class GetOrderSlowlyQueryHandler(IOrderService orderService) : IRequestHandler<GetOrderSlowlyQuery, Order?>
{
    public Task<Order?> Handle(GetOrderSlowlyQuery request, CancellationToken cancellationToken) =>
        orderService.GetSlowlyAsync(request.Id, cancellationToken);
}
