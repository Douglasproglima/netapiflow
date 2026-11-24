using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using NetApiFlow.Mcp.Endpoints;
using Shouldly;

namespace NetApiFlow.Mcp.Tests;

public class McpArgumentValidationTests
{
    [Fact]
    public void AddNetApiFlowMcp_WithNullServices_ThrowsNamingTheParameter()
    {
        Should.Throw<ArgumentNullException>(() => NetApiFlowMcpServiceCollectionExtensions.AddNetApiFlowMcp(null!))
            .ParamName.ShouldBe("services");
    }

    [Fact]
    public void MapNetApiFlowMcp_WithNullEndpoints_ThrowsNamingTheParameter()
    {
        Should.Throw<ArgumentNullException>(() => NetApiFlowMcpEndpointRouteBuilderExtensions.MapNetApiFlowMcp(null!))
            .ParamName.ShouldBe("endpoints");
    }

    [Fact]
    public async Task MapNetApiFlowMcp_WithANullBasePath_ThrowsNamingTheParameter()
    {
        await using var app = WebApplication.CreateBuilder().Build();

        Should.Throw<ArgumentNullException>(() => ((IEndpointRouteBuilder)app).MapNetApiFlowMcp(null!))
            .ParamName.ShouldBe("basePath");
    }
}
