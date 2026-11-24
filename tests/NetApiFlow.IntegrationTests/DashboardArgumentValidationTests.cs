using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class DashboardArgumentValidationTests
{
    [Fact]
    public void AddNetApiFlowDashboard_WithNullServices_ThrowsNamingTheParameter()
    {
        Should.Throw<ArgumentNullException>(() => NetApiFlowDashboardServiceCollectionExtensions.AddNetApiFlowDashboard(null!))
            .ParamName.ShouldBe("services");
    }

    [Fact]
    public void MapNetApiFlowDashboard_WithNullEndpoints_ThrowsNamingTheParameter()
    {
        Should.Throw<ArgumentNullException>(() => NetApiFlowDashboardEndpointRouteBuilderExtensions.MapNetApiFlowDashboard(null!))
            .ParamName.ShouldBe("endpoints");
    }

    [Fact]
    public async Task MapNetApiFlowDashboard_WithANullBasePath_ThrowsNamingTheParameter()
    {
        await using var app = WebApplication.CreateBuilder().Build();

        Should.Throw<ArgumentNullException>(() => ((IEndpointRouteBuilder)app).MapNetApiFlowDashboard(null!))
            .ParamName.ShouldBe("basePath");
    }
}
