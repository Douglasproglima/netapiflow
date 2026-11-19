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

    [Theory]
    [InlineData("api/orders/{id}", "/api/orders/{id:int}")]
    [InlineData("/api/orders/{id:guid}", "/api/orders/{id}")]
    [InlineData("/api/orders/{id:min(1)}", "/api/orders/{id}")]
    [InlineData("/api/orders/{id?}", "/api/orders/{id}")]
    [InlineData("/files/{*path}", "/files/{path}")]
    public void NormalizeForComparison_TreatsAConstrainedAndAnUnconstrainedParameterAsTheSameRoute(
        string left,
        string right)
    {
        RouteTemplateNormalizer.NormalizeForComparison(left)
            .ShouldBe(RouteTemplateNormalizer.NormalizeForComparison(right));
    }

    [Fact]
    public void NormalizeForComparison_KeepsTheConstraintOutOfTheKeyButNormalizeStillShowsIt()
    {
        const string routeTemplate = "/api/orders/{id:int}";

        RouteTemplateNormalizer.NormalizeForComparison(routeTemplate).ShouldBe("/api/orders/{id}");
        RouteTemplateNormalizer.Normalize(routeTemplate).ShouldBe(routeTemplate);
    }

    [Theory]
    [InlineData("/api/orders/{id:int}", "/api/orders/{slug}")]
    [InlineData("/api/orders/{id}", "/api/customers/{id}")]
    public void NormalizeForComparison_DifferentRoutes_AreNotEqual(string left, string right)
    {
        RouteTemplateNormalizer.NormalizeForComparison(left)
            .ShouldNotBe(RouteTemplateNormalizer.NormalizeForComparison(right));
    }
}
