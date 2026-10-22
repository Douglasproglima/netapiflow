using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetApiFlow.Instrumentation;
using NetApiFlow.MediatR;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.MediatR;

public sealed record Ping(string Message) : IRequest<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult(request.Message);
}

public class FlowTrackingBehaviorTests
{
    [Fact]
    public async Task Handle_WrapsHandlerExecution_RecordsUseCaseSpan()
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        using var listener = new FlowActivityListener(recorder, options);

        using var root = new Activity("TestRoot");
        root.Start();
        var traceId = root.TraceId.ToString();
        recorder.TryStartTrace(traceId, "GET", "/api/test", DateTime.UtcNow);

        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<PingHandler>());
        services.AddNetApiFlowMediatR();
        using var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var result = await mediator.Send(new Ping("hello"));

        result.ShouldBe("hello");
        var span = recorder.GetActiveTrace(traceId)!.Spans.Single(s => s.Type == ComponentType.UseCase);
        span.Component.ShouldBe(nameof(Ping));
        span.Status.ShouldBe(FlowStatus.Success);
    }
}
