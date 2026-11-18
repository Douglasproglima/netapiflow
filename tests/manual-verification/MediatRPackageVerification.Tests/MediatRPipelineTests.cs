using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.MediatR;
using Shouldly;
using Xunit;

namespace MediatRPackageVerification.Tests;

public sealed class MediatRPipelineTests
{
    [Fact]
    public async Task Send_ThroughTheTrackingBehavior_ReturnsTheHandlerResponse()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<GreetRequest>());
        services.AddNetApiFlowMediatR();

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        var response = await mediator.Send(new GreetRequest("world"));

        response.ShouldBe("Hello, world!");
    }

    [Fact]
    public async Task Send_WhenTheHandlerThrows_SurfacesTheHandlerException()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<GreetRequest>());
        services.AddNetApiFlowMediatR();

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => mediator.Send(new FailingRequest()));

        thrown.Message.ShouldBe("handler failed");
    }
}

public sealed record GreetRequest(string Name) : IRequest<string>;

public sealed class GreetRequestHandler : IRequestHandler<GreetRequest, string>
{
    public Task<string> Handle(GreetRequest request, CancellationToken cancellationToken) =>
        Task.FromResult($"Hello, {request.Name}!");
}

public sealed record FailingRequest : IRequest<string>;

public sealed class FailingRequestHandler : IRequestHandler<FailingRequest, string>
{
    public Task<string> Handle(FailingRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("handler failed");
}
