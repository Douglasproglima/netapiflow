using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.EntityFrameworkCore;
using NetApiFlow.Marking;
using NetApiFlow.MediatR;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests;

public class ArgumentValidationTests
{
    public static TheoryData<string, Action, string> NullArguments => new()
    {
        { "AddNetApiFlow", () => NetApiFlowServiceCollectionExtensions.AddNetApiFlow(null!), "services" },
        { "IncludePaths", () => new NetApiFlowOptions().IncludePaths(null!), "patterns" },
        { "ExcludePaths", () => new NetApiFlowOptions().ExcludePaths(null!), "patterns" },
        { "DecorateFlowTracked services", () => FlowTrackedServiceCollectionExtensions.DecorateFlowTracked(null!, _ => true), "services" },
        { "DecorateFlowTracked predicate", () => new ServiceCollection().DecorateFlowTracked(null!), "predicate" },
        { "TrackFlowByConvention", () => FlowTrackedServiceCollectionExtensions.TrackFlowByConvention(null!), "services" },
        { "WithNetApiFlowTracking", () => NetApiFlowEndpointFilterExtensions.WithNetApiFlowTracking<IEndpointConventionBuilder>(null!), "builder" },
        { "GetRecentTrace services", () => Flow.GetRecentTrace(null!, "trace"), "services" },
        { "GetRecentTrace traceId", () => Flow.GetRecentTrace(new ServiceCollection().BuildServiceProvider(), null!), "traceId" },
        { "AddNetApiFlowEntityFrameworkCore", () => NetApiFlowEntityFrameworkCoreServiceCollectionExtensions.AddNetApiFlowEntityFrameworkCore(null!), "services" },
        { "FlowDbCommandInterceptor", () => _ = new FlowDbCommandInterceptor(null!), "options" },
        { "AddNetApiFlowMediatR", () => NetApiFlowMediatRServiceCollectionExtensions.AddNetApiFlowMediatR(null!), "services" },
    };

    [Theory]
    [MemberData(nameof(NullArguments))]
    public void EntryPoint_WithANullArgument_ThrowsNamingTheParameter(string entryPoint, Action call, string parameter)
    {
        var exception = Should.Throw<ArgumentNullException>(call, entryPoint);

        exception.ParamName.ShouldBe(parameter);
    }
}
