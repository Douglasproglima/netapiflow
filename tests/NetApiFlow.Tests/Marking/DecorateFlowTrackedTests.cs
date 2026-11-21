using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Marking;
using NetApiFlow.Tests.Marking.ApplicationNamespace;
using NetApiFlow.Tests.Marking.OtherNamespace;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Marking;

public class DecorateFlowTrackedTests
{
    [Fact]
    public void DecorateFlowTracked_WithNamespacePredicate_OnlyDecoratesMatchingTypes()
    {
        var services = new ServiceCollection();
        services.AddScoped<IFoo, Foo>();
        services.AddScoped<IBar, Bar>();

        services.DecorateFlowTracked(type =>
            type.Namespace!.StartsWith("NetApiFlow.Tests.Marking.ApplicationNamespace", StringComparison.Ordinal));

        using var provider = services.BuildServiceProvider();
        var foo = provider.GetRequiredService<IFoo>();
        var bar = provider.GetRequiredService<IBar>();

        (foo is FlowInterceptorProxy).ShouldBeTrue();
        (bar is FlowInterceptorProxy).ShouldBeFalse();
        foo.Name().ShouldBe("foo");
        bar.Name().ShouldBe("bar");
    }

    [Fact]
    public void DecorateFlowTracked_PreservesOriginalLifetime()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFoo, Foo>();

        services.DecorateFlowTracked(type => type == typeof(Foo));

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IFoo>();
        var second = provider.GetRequiredService<IFoo>();

        ((FlowInterceptorProxy)(object)first).Target.ShouldBeSameAs(((FlowInterceptorProxy)(object)second).Target);
    }

    [Fact(Skip = "probe")]
    public void DecorateFlowTracked_ForADisposableScopedService_DisposesTheOriginalWhenTheScopeEnds()
    {
        var services = new ServiceCollection();
        services.AddScoped<IOwnedResource, OwnedResource>();
        services.DecorateFlowTracked(type => type == typeof(OwnedResource));
        using var provider = services.BuildServiceProvider();

        OwnedResource original;
        using (var scope = provider.CreateScope())
        {
            var resource = scope.ServiceProvider.GetRequiredService<IOwnedResource>();
            original = (OwnedResource)((FlowInterceptorProxy)(object)resource).Target;
            original.WasDisposed.ShouldBeFalse();
        }

        original.WasDisposed.ShouldBeTrue();
    }

    [Fact]
    public void DecorateFlowTracked_ForADisposableSingletonService_DisposesTheOriginalWhenTheProviderIsDisposed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOwnedResource, OwnedResource>();
        services.DecorateFlowTracked(type => type == typeof(OwnedResource));
        var provider = services.BuildServiceProvider();
        var original = (OwnedResource)((FlowInterceptorProxy)(object)provider.GetRequiredService<IOwnedResource>()).Target;
        original.WasDisposed.ShouldBeFalse();

        provider.Dispose();

        original.WasDisposed.ShouldBeTrue();
    }

    [Fact]
    public void DecorateFlowTracked_ForAnInstanceTheContainerDoesNotOwn_NeverDisposesIt()
    {
        var instance = new OwnedResource();
        var services = new ServiceCollection();
        services.AddSingleton<IOwnedResource>(instance);
        services.DecorateFlowTracked(type => type == typeof(OwnedResource));

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOwnedResource>();
        provider.Dispose();

        instance.WasDisposed.ShouldBeFalse();
    }
}
