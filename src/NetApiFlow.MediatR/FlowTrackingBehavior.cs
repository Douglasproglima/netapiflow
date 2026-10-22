using MediatR;

namespace NetApiFlow.MediatR;

/// <summary>Wraps every MediatR request handler execution in a <see cref="ComponentType.UseCase"/> step.</summary>
public sealed class FlowTrackingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        using var step = NetApiFlow.Step(typeof(TRequest).Name, "Handle", ComponentType.UseCase);

        try
        {
            return await next();
        }
        catch (Exception ex)
        {
            step.MarkError(ex);
            throw;
        }
    }
}
