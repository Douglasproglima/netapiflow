using MediatR;
using SampleApi.MinimalMediatR.Data;

namespace SampleApi.MinimalMediatR.Application;

/// <summary>Business-error scenario: fails mid-flow when the order is not Pending.</summary>
public sealed record ConfirmOrderCommand(int Id) : IRequest<Order>;

public sealed class ConfirmOrderCommandHandler(IOrderService orderService) : IRequestHandler<ConfirmOrderCommand, Order>
{
    public Task<Order> Handle(ConfirmOrderCommand request, CancellationToken cancellationToken) =>
        orderService.GetWithBusinessRuleAsync(request.Id, cancellationToken);
}
