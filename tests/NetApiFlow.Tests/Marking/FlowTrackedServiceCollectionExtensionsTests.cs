using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Marking;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Marking;

public class FlowTrackedServiceCollectionExtensionsTests
{
    public interface IGreeter
    {
        string Greet();
    }

    public sealed class Greeter : IGreeter
    {
        public string Greet() => "hi";
    }

    [Fact]
    public void AddFlowTracked_WithScopedLifetime_ResolvesSameUnderlyingInstanceTwiceInSameScope()
    {
        var services = new ServiceCollection();
        services.AddFlowTracked<IGreeter, Greeter>(ComponentType.Service, ServiceLifetime.Scoped);
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var first = scope.ServiceProvider.GetRequiredService<IGreeter>();
        var second = scope.ServiceProvider.GetRequiredService<IGreeter>();

        // The proxy object itself is cached per scope (same as any Scoped service), but the
        // identity that actually matters is the decorated target behind it: DispatchProxy.Create
        // would hand back a fresh wrapper every time it is called directly, so the test asserts on
        // FlowInterceptorProxy.Target (the underlying Greeter instance), not proxy reference equality.
        var firstTarget = ((FlowInterceptorProxy)(object)first).Target;
        var secondTarget = ((FlowInterceptorProxy)(object)second).Target;

        firstTarget.ShouldBeSameAs(secondTarget);
    }

    [Fact]
    public void AddFlowTracked_WithScopedLifetime_ResolvesDifferentUnderlyingInstanceAcrossScopes()
    {
        var services = new ServiceCollection();
        services.AddFlowTracked<IGreeter, Greeter>(ComponentType.Service, ServiceLifetime.Scoped);
        using var provider = services.BuildServiceProvider();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();
        var fromScopeA = ((FlowInterceptorProxy)(object)scopeA.ServiceProvider.GetRequiredService<IGreeter>()).Target;
        var fromScopeB = ((FlowInterceptorProxy)(object)scopeB.ServiceProvider.GetRequiredService<IGreeter>()).Target;

        fromScopeA.ShouldNotBeSameAs(fromScopeB);
    }

    [Fact]
    public void AddFlowTracked_ResolvedInterface_ForwardsCallsToTheRealImplementation()
    {
        var services = new ServiceCollection();
        services.AddFlowTracked<IGreeter, Greeter>(ComponentType.Service);
        using var provider = services.BuildServiceProvider();

        var greeter = provider.GetRequiredService<IGreeter>();

        greeter.Greet().ShouldBe("hi");
    }
}
