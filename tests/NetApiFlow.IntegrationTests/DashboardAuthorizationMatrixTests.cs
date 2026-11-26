using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.TestSupport;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class DashboardAuthorizationMatrixTests
{
    private const string BasePath = "/flow";

    [Theory]
    [MemberData(nameof(AuthorizationMatrix.Names), MemberType = typeof(AuthorizationMatrix))]
    public async Task EveryDashboardRouteAndMethod_FollowsTheMatrix(string caseName)
    {
        var matrixCase = AuthorizationMatrix.ByName(caseName);
        var openDevelopment = await RunAsync(AuthorizationMatrix.Cases[0]);
        var answers = await RunAsync(matrixCase);

        AuthorizationMatrix.Violations(matrixCase, answers, openDevelopment).ShouldBeEmpty();
    }

    [Fact]
    public async Task TheEnumeratedRoutes_CoverTheApiTheStreamTheStaticFilesAndTheIndex()
    {
        var answers = await RunAsync(AuthorizationMatrix.Cases[0]);
        var requests = answers.Select(answer => $"{answer.Request.Method} {answer.Request.Path}").ToList();

        requests.ShouldContain("GET /flow/api/traces");
        requests.ShouldContain("DELETE /flow/api/traces");
        requests.ShouldContain("GET /flow/api/stream");
        requests.ShouldContain("GET /flow/");
        requests.ShouldContain("GET /flow/index.html");
        answers.Any(answer => answer.Request.Method == "POST").ShouldBeTrue("the API has a POST route");
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

    [Theory]
    [InlineData("production, nothing configured", "{}", "application/json", 404)]
    [InlineData("production, nothing configured", "{\"label\":", "application/json", 404)]
    [InlineData("production, nothing configured", "not json at all", "application/json", 404)]
    [InlineData("production, nothing configured", "{\"label\":\"x\"}", "text/plain", 404)]
    [InlineData("production, access filter denies", "{}", "application/json", 403)]
    [InlineData("production, access filter denies", "{\"label\":", "application/json", 403)]
    [InlineData("production, access filter denies", "{\"label\":\"x\"}", "text/plain", 403)]
    [InlineData("production, AllowAnonymous alone", "{}", "application/json", 404)]
    [InlineData("production, RequireAuthorization, caller lacks the claim", "{}", "application/json", 403)]
    [InlineData("development, nothing configured", "{}", "application/json", 400)]
    [InlineData("development, nothing configured", "{\"label\":", "application/json", 400)]
    [InlineData("development, nothing configured", "{\"label\":\"x\"}", "text/plain", 415)]
    [InlineData("production, access filter allows", "{}", "application/json", 400)]
    [InlineData("development, nothing configured", "{\"label\":\"x\"}", "application/json", 201)]
    public async Task APostToTheDeploys_WithABodyTheHandlerRejects_IsAnsweredByTheGateFirst(
        string caseName,
        string body,
        string contentType,
        int expectedStatus)
    {
        var status = await AuthorizationMatrix.PostAsync(
            AuthorizationMatrix.ByName(caseName),
            services => services.AddNetApiFlowDashboard(),
            endpoints => endpoints.MapNetApiFlowDashboard(),
            "/flow/api/deploys",
            body,
            contentType);

        status.ShouldBe(expectedStatus);
    }

    [Theory]
    [InlineData("production, nothing configured")]
    [InlineData("production, access filter denies")]
    [InlineData("production, RequireAuthorization, caller lacks the claim")]
    [InlineData("production, AllowAnonymous alone")]
    public async Task EveryGetRouteWithMalformedQueryValues_IsAnsweredByTheGate(string caseName)
    {
        const string malformed = "?status=banana&from=notadate&to=notadate&minDurationMicros=abc&page=abc&pageSize=abc&sort=zzz&order=zzz";
        var matrixCase = AuthorizationMatrix.ByName(caseName);
        var openDevelopment = await AuthorizationMatrix.RunAsync(
            AuthorizationMatrix.Cases[0],
            BasePath,
            services => services.AddNetApiFlowDashboard(),
            endpoints => endpoints.MapNetApiFlowDashboard(),
            getQuery: malformed);
        var answers = await AuthorizationMatrix.RunAsync(
            matrixCase,
            BasePath,
            services => services.AddNetApiFlowDashboard(),
            endpoints => endpoints.MapNetApiFlowDashboard(),
            getQuery: malformed);

        AuthorizationMatrix.Violations(matrixCase, answers, openDevelopment).ShouldBeEmpty();
        answers.Any(answer => answer.Request.Method == "GET" && answer.Request.Path.Contains("page=abc", StringComparison.Ordinal)).ShouldBeTrue();
    }

    private static void AddARouteWithoutTheGate(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/flow/forgotten-route", () => "served to everyone");

    private static Task<IReadOnlyList<MatrixAnswer>> RunAsync(AuthorizationMatrixCase matrixCase, Action<IEndpointRouteBuilder>? mapExtra = null) =>
        AuthorizationMatrix.RunAsync(
            matrixCase,
            BasePath,
            services => services.AddNetApiFlowDashboard(),
            endpoints => endpoints.MapNetApiFlowDashboard(),
            mapExtra);
}
