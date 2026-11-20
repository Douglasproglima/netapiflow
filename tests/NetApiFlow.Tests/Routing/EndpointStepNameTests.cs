using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using NetApiFlow.Routing;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Routing;

public class EndpointStepNameTests
{
    private static readonly RequestDelegate NoOp = _ => Task.CompletedTask;

    [Fact]
    public void For_WithoutAnEndpoint_NamesItEndpoint()
    {
        var name = EndpointStepName.For(null);

        name.Component.ShouldBe("Minimal API");
        name.Operation.ShouldBe("Endpoint");
    }

    [Fact]
    public void For_AnEndpointThatIsNotARouteEndpoint_UsesItsDisplayNameAsTheOperation()
    {
        var endpoint = new Endpoint(NoOp, EndpointMetadataCollection.Empty, "a display name");

        var name = EndpointStepName.For(endpoint);

        name.Component.ShouldBe("Minimal API");
        name.Operation.ShouldBe("a display name");
    }

    [Fact]
    public void For_ARouteWithoutVerbMetadata_UsesOnlyTheNormalizedRoute()
    {
        var endpoint = new RouteEndpoint(
            NoOp,
            RoutePatternFactory.Parse("fallback/{*path}"),
            order: 0,
            EndpointMetadataCollection.Empty,
            "fallback");

        var name = EndpointStepName.For(endpoint);

        name.Component.ShouldBe("Minimal API");
        name.Operation.ShouldBe("/fallback/{*path}");
    }

    [Fact]
    public void For_AnEndpointNameWithoutAHandlerMethod_UsesTheEndpointName()
    {
        var endpoint = new RouteEndpoint(
            NoOp,
            RoutePatternFactory.Parse("/orders"),
            order: 0,
            new EndpointMetadataCollection(new EndpointNameMetadata("ListOrders"), new HttpMethodMetadata(["GET"])),
            "orders");

        var name = EndpointStepName.For(endpoint);

        name.Component.ShouldBe("Minimal API");
        name.Operation.ShouldBe("ListOrders");
    }

    [Fact]
    public void For_SeveralVerbs_JoinsThemInDeclarationOrderBeforeTheRoute()
    {
        var endpoint = new RouteEndpoint(
            NoOp,
            RoutePatternFactory.Parse("/orders"),
            order: 0,
            new EndpointMetadataCollection(new HttpMethodMetadata(["PUT", "GET"])),
            "orders");

        var name = EndpointStepName.For(endpoint);

        name.Operation.ShouldBe("PUT, GET /orders");
    }
}
