using NetApiFlow.Routing;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Routing;

public class RouteTemplateNormalizerTests
{
    [Theory]
    [InlineData("api/x", "/api/x")]
    [InlineData("/api/x", "/api/x")]
    [InlineData("/api/x/", "/api/x")]
    [InlineData("/", "/")]
    [InlineData("api/orders/{id:int}", "/api/orders/{id:int}")]
    public void Normalize_ReturnsALeadingSlashAndNeverATrailingOne(string routeTemplate, string expected)
    {
        RouteTemplateNormalizer.Normalize(routeTemplate).ShouldBe(expected);
    }

    [Fact]
    public void Normalize_EmptyString_ReturnsRoot()
    {
        RouteTemplateNormalizer.Normalize("").ShouldBe("/");
    }
}
