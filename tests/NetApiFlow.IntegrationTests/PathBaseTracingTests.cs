using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using NetApiFlow.Domain;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class PathBaseTracingTests
{
    private const string PathBaseOfTheApp = "/api";

    public static TheoryData<string, string> Hosts { get; } = new()
    {
        { string.Empty, Environments.Development },
        { string.Empty, Environments.Production },
        { PathBaseOfTheApp, Environments.Development },
        { PathBaseOfTheApp, Environments.Production },
    };

    public static TheoryData<string, string, string> WaysToExclude { get; } = BuildWaysToExclude();

    private static TheoryData<string, string, string> BuildWaysToExclude()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var way in new[] { "endpoint", "group", "controllers", "healthchecks", "over-include-paths", "over-with-tracking" })
        {
            foreach (var pathBase in new[] { string.Empty, PathBaseOfTheApp })
            {
                foreach (var environment in new[] { Environments.Development, Environments.Production })
                {
                    data.Add(way, pathBase, environment);
                }
            }
        }

        return data;
    }

    private static PathBaseSetup Excluding(string way, string pathBase, string environment) => way switch
    {
        "endpoint" => new PathBaseSetup
        {
            PathBase = pathBase,
            Environment = environment,
            Map = endpoints => endpoints.MapGet("/excluded", () => "ok").WithoutNetApiFlowTracking(),
        },
        "group" => new PathBaseSetup
        {
            PathBase = pathBase,
            Environment = environment,
            Map = endpoints => endpoints.MapGroup("/group").WithoutNetApiFlowTracking().MapGet("/item", () => "ok"),
        },
        "controllers" => new PathBaseSetup
        {
            PathBase = pathBase,
            Environment = environment,
            Controllers = true,
            Map = endpoints => endpoints.MapControllers().WithoutNetApiFlowTracking(),
        },
        "healthchecks" => new PathBaseSetup
        {
            PathBase = pathBase,
            Environment = environment,
            Services = services => services.AddHealthChecks(),
            Map = endpoints => endpoints.MapHealthChecks("/excluded").WithoutNetApiFlowTracking(),
        },
        "over-include-paths" => new PathBaseSetup
        {
            PathBase = pathBase,
            Environment = environment,
            Configure = options => options.IncludePaths("/excluded", PathBaseApp.ControlRoute),
            Map = endpoints => endpoints.MapGet("/excluded", () => "ok").WithoutNetApiFlowTracking(),
        },
        "over-with-tracking" => new PathBaseSetup
        {
            PathBase = pathBase,
            Environment = environment,
            Map = endpoints => endpoints.MapGet("/excluded", () => "ok").WithNetApiFlowTracking().WithoutNetApiFlowTracking(),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(way), way, null),
    };

    private static string PathOfTheExcludedRoute(string way) => way switch
    {
        "group" => "/group/item",
        "controllers" => "/api/orders/7",
        _ => "/excluded",
    };

    private static void ShouldHaveLeftNoTrace(PathBaseApp app, HttpResponseMessage response)
    {
        response.Headers.Contains(PathBaseApp.TraceIdHeader).ShouldBeFalse();
        app.StartedTraces.ShouldBeEmpty();
        app.StartedSpans.ShouldBeEmpty();
        app.CompletedTraces.ShouldBeEmpty();
        app.Recorder.ActiveTraceCount.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task RoutesOfTheDashboard_AreNeverTraced(string pathBase, string environment)
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup { PathBase = pathBase, Environment = environment, Dashboard = true });

        foreach (var path in new[] { "/flow/api/config", "/flow/api/traces", "/flow/api/traces/live", "/flow/", "/flow/some/client/route" })
        {
            var response = await app.GetAsync(path);

            response.Headers.Contains(PathBaseApp.TraceIdHeader).ShouldBeFalse($"{pathBase}{path} answered with a trace id");
        }

        app.StartedTraces.ShouldBeEmpty();
        app.StartedSpans.ShouldBeEmpty();
        app.Recorder.ActiveTraceCount.ShouldBe(0);
        (await app.RoutesInTheStoreAsync()).ShouldBe([PathBaseApp.ControlRoute]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(PathBaseOfTheApp)]
    public async Task StreamOfTheDashboard_IsNeverTraced_AndNeverShowsAsRunning(string pathBase)
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup { PathBase = pathBase, Dashboard = true });
        using var request = new HttpRequestMessage(HttpMethod.Get, pathBase + "/flow/api/stream");

        using var response = await app.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains(PathBaseApp.TraceIdHeader).ShouldBeFalse();
        app.StartedTraces.ShouldBeEmpty();
        app.Recorder.ActiveTraceCount.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task InitializeOfTheMcpServer_IsNeverTraced(string pathBase, string environment)
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup { PathBase = pathBase, Environment = environment, Mcp = true });
        using var request = new HttpRequestMessage(HttpMethod.Post, pathBase + "/flow/mcp")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2026-01-01","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await app.Client.SendAsync(request);

        response.StatusCode.ShouldBe(environment == Environments.Development ? HttpStatusCode.OK : HttpStatusCode.NotFound);
        ShouldHaveLeftNoTrace(app, response);
        (await app.RoutesInTheStoreAsync()).ShouldBe([PathBaseApp.ControlRoute]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(PathBaseOfTheApp)]
    public async Task ToolCallOfTheMcpServer_IsNeverTraced(string pathBase)
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup { PathBase = pathBase, Mcp = true });
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(app.Client.BaseAddress!, pathBase + "/flow/mcp") },
            app.Client,
            ownsHttpClient: false);
        await using var mcp = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);

        var result = await mcp.CallToolAsync("list_recent_traces", cancellationToken: CancellationToken.None);

        result.IsError.ShouldNotBe(true);
        app.StartedTraces.ShouldBeEmpty();
        app.StartedSpans.ShouldBeEmpty();
        app.Recorder.ActiveTraceCount.ShouldBe(0);
        (await app.RoutesInTheStoreAsync()).ShouldBe([PathBaseApp.ControlRoute]);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task ExcludePaths_OnAMatchedEndpoint_ValuesThePathTheUserSees(string pathBase, string environment)
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            PathBase = pathBase,
            Environment = environment,
            Configure = options => options.ExcludePaths("/health"),
            Map = endpoints =>
            {
                endpoints.MapGet("/health", () => "ok");
                endpoints.MapGet("/orders", () => "ok");
            },
        });

        var excluded = await app.GetAsync("/health");
        ShouldHaveLeftNoTrace(app, excluded);
        var traced = await app.GetAsync("/orders");

        traced.Headers.Contains(PathBaseApp.TraceIdHeader).ShouldBeTrue();
        (await app.RoutesInTheStoreAsync()).ShouldBe(["/control", "/orders"]);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task IncludePaths_OnAMatchedEndpoint_KeepsTheUsersRouteInTheStore(string pathBase, string environment)
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            PathBase = pathBase,
            Environment = environment,
            Configure = options => options.IncludePaths("/orders", PathBaseApp.ControlRoute),
            Map = endpoints =>
            {
                endpoints.MapGet("/orders", () => "ok");
                endpoints.MapGet("/other", () => "ok");
            },
        });

        var included = await app.GetAsync("/orders");
        included.Headers.Contains(PathBaseApp.TraceIdHeader).ShouldBeTrue();
        var notIncluded = await app.GetAsync("/other");
        notIncluded.Headers.Contains(PathBaseApp.TraceIdHeader).ShouldBeFalse();

        (await app.RoutesInTheStoreAsync()).ShouldBe(["/control", "/orders"]);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task OpenApiDocumentPath_IsNeverTraced(string pathBase, string environment)
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            PathBase = pathBase,
            Environment = environment,
            Map = endpoints => endpoints.MapGet("/openapi/v1.json", () => Results.Json(new { openapi = "3.0.1" })),
        });

        var response = await app.GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldHaveLeftNoTrace(app, response);
        (await app.RoutesInTheStoreAsync()).ShouldBe([PathBaseApp.ControlRoute]);
    }

    [Theory]
    [MemberData(nameof(WaysToExclude))]
    public async Task EndpointWithoutNetApiFlowTracking_IsNeverTraced(string way, string pathBase, string environment)
    {
        await using var app = await PathBaseApp.StartAsync(Excluding(way, pathBase, environment));

        var response = await app.GetAsync(PathOfTheExcludedRoute(way));

        response.StatusCode.ShouldNotBe(HttpStatusCode.NotFound, $"the route of '{way}' was not mapped");
        ShouldHaveLeftNoTrace(app, response);
        (await app.RoutesInTheStoreAsync()).ShouldBe([PathBaseApp.ControlRoute]);
    }

    [Fact]
    public async Task StaticFile_ExcludedByItsPath_WithoutPathBase_IsNotTraced()
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            Configure = options => options.ExcludePaths("/excluded.css"),
            StaticFiles = new SingleFileProvider("excluded.css", "body { color: black; }"),
        });

        var response = await app.GetAsync("/excluded.css");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldHaveLeftNoTrace(app, response);
        (await app.RoutesInTheStoreAsync()).ShouldBe([PathBaseApp.ControlRoute]);
    }

    [Fact]
    public async Task StaticFile_WithoutAMatchedEndpoint_UnderPathBase_IsDecidedByTheFullPath()
    {
        await using var withoutThePrefix = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            PathBase = PathBaseOfTheApp,
            Configure = options => options.ExcludePaths("/excluded.css"),
            StaticFiles = new SingleFileProvider("excluded.css", "body { color: black; }"),
        });
        await using var withThePrefix = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            PathBase = PathBaseOfTheApp,
            Configure = options => options.ExcludePaths($"{PathBaseOfTheApp}/excluded.css"),
            StaticFiles = new SingleFileProvider("excluded.css", "body { color: black; }"),
        });

        var traced = await withoutThePrefix.GetAsync("/excluded.css");
        var excluded = await withThePrefix.GetAsync("/excluded.css");

        traced.StatusCode.ShouldBe(HttpStatusCode.OK);
        traced.Headers.Contains(PathBaseApp.TraceIdHeader).ShouldBeTrue();
        (await withoutThePrefix.RoutesInTheStoreAsync()).ShouldBe(["(unmatched)", PathBaseApp.ControlRoute]);
        excluded.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldHaveLeftNoTrace(withThePrefix, excluded);
        (await withThePrefix.RoutesInTheStoreAsync()).ShouldBe([PathBaseApp.ControlRoute]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(PathBaseOfTheApp)]
    public async Task RequestWithoutAMatchedEndpoint_HasTheHeaderExactlyWhenItIsRecorded(string pathBase)
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            PathBase = pathBase,
            Configure = options => options.ExcludePaths($"{pathBase}/skipped"),
            StaticFiles = new SingleFileProvider("site.css", "body { color: black; }"),
        });

        var recorded = await app.GetAsync("/site.css");
        var notFound = await app.GetAsync("/nothing-matches-this");
        var skipped = await app.GetAsync("/skipped");

        recorded.Headers.Contains(PathBaseApp.TraceIdHeader).ShouldBeTrue();
        notFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        notFound.Headers.Contains(PathBaseApp.TraceIdHeader).ShouldBeTrue();
        skipped.Headers.Contains(PathBaseApp.TraceIdHeader).ShouldBeFalse();
        app.StartedTraces.Count.ShouldBe(2);
        app.StartedTraces.ShouldAllBe(trace => trace.RouteTemplate == "(unmatched)");
        app.Recorder.ActiveTraceCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("/orders")]
    [InlineData("/nothing-matches-this")]
    public async Task PolicyThatThrows_StillAnswersTheRequest_AndOpensNoTrace(string path)
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            Configure = options => options.IncludePaths(new string[] { null! }),
            Map = endpoints => endpoints.MapGet("/orders", () => "ok"),
        });

        var response = await app.GetAsync(path);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
        ShouldHaveLeftNoTrace(app, response);
    }

    [Theory]
    [InlineData("")]
    [InlineData(PathBaseOfTheApp)]
    public async Task StepOpenedBeforeRouting_IsKeptInTheTraceOfATracedRequest(string pathBase)
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            PathBase = pathBase,
            BeforeRouting = application => application.Use(async (context, next) =>
            {
                using var step = NetApiFlow.Step("PreRoutingMiddleware", "Run", ComponentType.Service);
                await next();
            }),
            Map = endpoints => endpoints.MapGet("/orders", () => "ok"),
        });

        var response = await app.GetAsync("/orders");

        var traceId = response.Headers.GetValues(PathBaseApp.TraceIdHeader).Single();
        var detail = app.Recorder.GetRecentlyCompleted(traceId).ShouldNotBeNull();
        var preRouting = detail.Spans.Single(span => span.Component == "PreRoutingMiddleware");
        detail.Spans.Single(span => span.Type == ComponentType.Endpoint).ParentSpanId.ShouldBe(preRouting.SpanId);
        detail.Trace.RouteTemplate.ShouldBe("/orders");
        app.Recorder.ActiveTraceCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData(PathBaseOfTheApp)]
    public async Task ExcludedRequest_WithAStepOpenedBeforeRouting_PublishesNothing(string pathBase)
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            PathBase = pathBase,
            BeforeRouting = application => application.Use(async (context, next) =>
            {
                using var step = NetApiFlow.Step("PreRoutingMiddleware", "Run", ComponentType.Service);
                await next();
            }),
            Map = endpoints => endpoints.MapGet("/excluded", () => "ok").WithoutNetApiFlowTracking(),
        });

        var response = await app.GetAsync("/excluded");

        ShouldHaveLeftNoTrace(app, response);
    }

    [Fact]
    public async Task RequestWithoutAMatchedEndpoint_ExcludedByItsFullPath_PublishesNothingAndLeavesNoReservation()
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            Configure = options => options.ExcludePaths("/nothing-matches-this"),
            BeforeRouting = application => application.Use(async (context, next) =>
            {
                using var step = NetApiFlow.Step("PreRoutingMiddleware", "Run", ComponentType.Service);
                await next();
            }),
        });

        var response = await app.GetAsync("/nothing-matches-this");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ShouldHaveLeftNoTrace(app, response);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExceptionBeforeRouting_LeavesNoReservation(bool onAnExcludedPath)
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            Configure = options =>
            {
                if (onAnExcludedPath)
                {
                    options.ExcludePaths("/orders");
                }
            },
            BeforeRouting = application => application.Use((HttpContext context, RequestDelegate next) => throw new InvalidOperationException("boom before routing")),
            Map = endpoints => endpoints.MapGet("/orders", () => "ok"),
        });

        await Should.ThrowAsync<InvalidOperationException>(() => app.GetAsync("/orders"));

        app.Recorder.ActiveTraceCount.ShouldBe(0);
        if (onAnExcludedPath)
        {
            app.StartedTraces.ShouldBeEmpty();
        }
    }
}
