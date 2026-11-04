using Microsoft.AspNetCore.Mvc;
using SampleApi.Controllers.Application;

namespace SampleApi.Controllers.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(IOrderService orderService) : ControllerBase
{
    /// <summary>Success scenario: fetches an existing order.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var order = await orderService.GetAsync(id, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>Business-error scenario: fails mid-flow when the order is not Pending.</summary>
    [HttpGet("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken cancellationToken)
    {
        var order = await orderService.GetWithBusinessRuleAsync(id, cancellationToken);
        return Ok(order);
    }

    /// <summary>Slow scenario: takes longer than the configured SlowThreshold.</summary>
    [HttpGet("{id:int}/slow")]
    public async Task<IActionResult> GetSlowly(int id, CancellationToken cancellationToken)
    {
        var order = await orderService.GetSlowlyAsync(id, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>Parallel scenario: fetches several orders concurrently via <see cref="Task.WhenAll"/>.</summary>
    [HttpGet("parallel")]
    public async Task<IActionResult> GetParallel([FromQuery] int[] ids, CancellationToken cancellationToken)
    {
        var orders = await orderService.GetManyInParallelAsync(ids, cancellationToken);
        return Ok(orders);
    }

    /// <summary>External call scenario: calls an outbound HTTP service, observed via <c>ExternalSources.HttpClient</c>.</summary>
    [HttpGet("{id:int}/shipping-status")]
    public async Task<IActionResult> GetShippingStatus(int id, CancellationToken cancellationToken)
    {
        var status = await orderService.GetShippingStatusAsync(id, cancellationToken);
        return Ok(status);
    }
}
