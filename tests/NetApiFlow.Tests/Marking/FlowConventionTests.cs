using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Marking;
using NetApiFlow.MediatR;
using NetApiFlow.Tests.Marking.ApplicationNamespace;
using NetApiFlow.Tests.Marking.OtherNamespace;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Marking;

public sealed class FlowConventionTests
{
    private static readonly Assembly ThisAssembly = typeof(FlowConventionTests).Assembly;

    [Fact]
    public void Includes_WithNothingConfigured_AcceptsOnlyTheEntryAssembly()
    {
        var subject = new FlowConventionOptions();

        subject.Includes(typeof(Foo), ThisAssembly).ShouldBeTrue();
        subject.Includes(typeof(FlowStep), ThisAssembly).ShouldBeFalse();
        subject.Includes(typeof(Foo), null).ShouldBeFalse();
    }

    [Fact]
    public void IncludeAssembly_ReplacesTheDefaultScopeInsteadOfAddingToIt()
    {
        var subject = new FlowConventionOptions().IncludeAssembly(typeof(FlowStep).Assembly);

        subject.Includes(typeof(FlowStep), ThisAssembly).ShouldBeTrue();
        subject.Includes(typeof(Foo), ThisAssembly).ShouldBeFalse();
    }

    [Theory]
    [InlineData("NetApiFlow.Tests.Marking.ApplicationNamespace", true)]
    [InlineData("NetApiFlow.Tests.Marking.ApplicationNamespace.", true)]
    [InlineData("NetApiFlow.Tests.Marking", true)]
    [InlineData("NetApiFlow.Tests.Marking.Application", false)]
    [InlineData("NetApiFlow.Tests.Marking.OtherNamespace", false)]
    public void IncludeNamespacePrefix_MatchesWholeNamespaceSegmentsOnly(string prefix, bool includesTheType)
    {
        var subject = new FlowConventionOptions().IncludeNamespacePrefix(prefix);

        subject.Includes(typeof(Foo), ThisAssembly).ShouldBe(includesTheType);
    }

    [Fact]
    public void Includes_WithAnAssemblyAndANamespacePrefix_AcceptsWhatEitherOneCovers()
    {
        var subject = new FlowConventionOptions()
            .IncludeAssembly(typeof(FlowStep).Assembly)
            .IncludeNamespacePrefix("NetApiFlow.Tests.Marking.ApplicationNamespace");

        subject.Includes(typeof(FlowStep), ThisAssembly).ShouldBeTrue();
        subject.Includes(typeof(Foo), ThisAssembly).ShouldBeTrue();
        subject.Includes(typeof(Bar), ThisAssembly).ShouldBeFalse();
    }

    [Theory]
    [InlineData(typeof(SampleGreetingService), ComponentType.Service)]
    [InlineData(typeof(SampleGreetingRepository), ComponentType.Repository)]
    [InlineData(typeof(SampleGreetingHandler), ComponentType.UseCase)]
    public void ComponentTypeOf_ForANameWithAConventionSuffix_ReturnsItsComponentType(Type type, ComponentType expected)
    {
        FlowConventions.ComponentTypeOf(type).ShouldBe(expected);
    }

    [Theory]
    [InlineData(typeof(SampleGreetingServiceFactory))]
    [InlineData(typeof(ServiceLocatorOfSomething))]
    [InlineData(typeof(Foo))]
    public void ComponentTypeOf_ForAnyOtherName_ReturnsNothing(Type type)
    {
        FlowConventions.ComponentTypeOf(type).ShouldBeNull();
    }

    [Theory]
    [InlineData(typeof(FlowStep), true)]
    [InlineData(typeof(FlowTrackingBehavior<,>), true)]
    [InlineData(typeof(Foo), false)]
    [InlineData(typeof(string), false)]
    public void Is_ForATypeOfANetApiFlowPackage_ReturnsTrue(Type type, bool expected)
    {
        NetApiFlowOwnTypes.Is(type).ShouldBe(expected);
    }

    [Fact]
    public void TrackFlowByConvention_CalledTwice_DecoratesOnce()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISampleGreetingService, SampleGreetingService>();

        FlowTrackedServiceCollectionExtensions.TrackFlowByConventionOf(services, null, ThisAssembly);
        FlowTrackedServiceCollectionExtensions.TrackFlowByConventionOf(services, null, ThisAssembly);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetRequiredService<ISampleGreetingService>();
        ((FlowInterceptorProxy)(object)resolved).Target.ShouldBeOfType<SampleGreetingService>();
    }

    [Fact]
    public void DecorateFlowTracked_WithAPredicateThatMatchesEverything_NeverReplacesADescriptorOfTheCore()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNetApiFlow();
        var ofTheCore = services.Where(descriptor => descriptor.ImplementationType is { } type && NetApiFlowOwnTypes.Is(type)).ToList();

        services.DecorateFlowTracked(_ => true);

        ofTheCore.ShouldNotBeEmpty();
        foreach (var descriptor in ofTheCore)
        {
            services.ShouldContain(descriptor);
        }
    }

    [Fact]
    public void DecorateFlowTracked_WithAPredicateThatMatchesAnOpenGenericRegistration_LeavesItAsItWasAndDoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ISampleOpenGeneric<>), typeof(SampleOpenGeneric<>));
        var registered = services.Single();

        services.DecorateFlowTracked(type => type == typeof(SampleOpenGeneric<>));

        services.ShouldHaveSingleItem().ShouldBeSameAs(registered);
    }

    public interface ISampleOpenGeneric<T>;

    public sealed class SampleOpenGeneric<T> : ISampleOpenGeneric<T>;

    public interface ISampleGreetingService
    {
        string Greet();
    }

    public sealed class SampleGreetingService : ISampleGreetingService
    {
        public string Greet() => "hello";
    }

    public sealed class SampleGreetingRepository;

    public sealed class SampleGreetingHandler;

    public sealed class SampleGreetingServiceFactory;

    public sealed class ServiceLocatorOfSomething;
}
