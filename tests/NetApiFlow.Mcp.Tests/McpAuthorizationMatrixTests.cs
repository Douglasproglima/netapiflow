using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using NetApiFlow.Mcp;
using NetApiFlow.Mcp.Endpoints;
using NetApiFlow.TestSupport;
using Shouldly;
using Xunit;

namespace NetApiFlow.Mcp.Tests;

public class McpAuthorizationMatrixTests
{
    private const string BasePath = "/flow";

    [Theory]
    [MemberData(nameof(AuthorizationMatrix.Names), MemberType = typeof(AuthorizationMatrix))]
    public async Task EveryMcpRouteAndMethod_FollowsTheMatrix(string caseName)
    {
        var matrixCase = AuthorizationMatrix.ByName(caseName);
        var openDevelopment = await RunAsync(AuthorizationMatrix.Cases[0]);
        var answers = await RunAsync(matrixCase);

        AuthorizationMatrix.Violations(matrixCase, answers, openDevelopment).ShouldBeEmpty();
    }

    [Fact]
    public async Task TheEnumeratedRoutes_CoverThePostOfTheMcpEndpoint()
    {
        var answers = await RunAsync(AuthorizationMatrix.Cases[0]);

        answers.Select(answer => $"{answer.Request.Method} {answer.Request.Path}").ShouldContain("POST /flow/mcp/");
    }

    [Fact]
    public async Task ARouteUnderTheBasePathWithoutTheGate_IsCaughtByTheMatrix()
    {
        var closedProduction = AuthorizationMatrix.ByName("production, nothing configured");
        var openDevelopment = await RunAsync(AuthorizationMatrix.Cases[0], mapExtra: AddARouteWithoutTheGate);
        var answers = await RunAsync(closedProduction, mapExtra: AddARouteWithoutTheGate);

        var violations = AuthorizationMatrix.Violations(closedProduction, answers, openDevelopment);

        violations.ShouldHaveSingleItem().ShouldStartWith("GET /flow/forgotten-route answered 200");
    }

    private static void AddARouteWithoutTheGate(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/flow/forgotten-route", () => "served to everyone");

    private static Task<IReadOnlyList<MatrixAnswer>> RunAsync(AuthorizationMatrixCase matrixCase, Action<IEndpointRouteBuilder>? mapExtra = null) =>
        AuthorizationMatrix.RunAsync(
            matrixCase,
            BasePath,
            services => services.AddNetApiFlowMcp(),
            endpoints => endpoints.MapNetApiFlowMcp(),
            mapExtra);
}
