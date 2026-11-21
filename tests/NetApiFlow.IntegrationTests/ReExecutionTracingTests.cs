using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NetApiFlow.Domain;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class ReExecutionTracingTests
{
    private static PathBaseSetup WithStatusCodePages(Action<NetApiFlowOptions> configure) => new()
    {
        Configure = configure,
        BeforeRouting = app => app.UseStatusCodePagesWithReExecute("/error/{0}"),
        Map = endpoints => endpoints.MapGet("/error/{code}", (string code) => $"error {code}"),
    };

    private static PathBaseSetup WithExceptionHandler(Action<NetApiFlowOptions> configure) => new()
    {
        Configure = configure,
        BeforeRouting = app =>
        {
            app.UseExceptionHandler("/error");
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.Value!.EndsWith("/crash", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The request failed before routing.");
                }

                await next();
            });
        },
        Map = endpoints => endpoints.MapGet("/error", () => "error page"),
    };

    private static async Task ShouldHaveLeftNoTraceAsync(PathBaseApp app, HttpResponseMessage response)
    {
        response.Headers.Contains(PathBaseApp.TraceIdHeader).ShouldBeFalse();
        app.StartedTraces.ShouldBeEmpty();
        app.CompletedTraces.ShouldBeEmpty();
        app.Recorder.ActiveTraceCount.ShouldBe(0);
        (await app.RoutesInTheStoreAsync()).ShouldBe(["/control"]);
    }

    [Fact]
    public async Task StatusCodePageReExecution_WhenOnlyTheErrorPathIsExcluded_StillRecordsTheRequest()
    {
        await using var app = await PathBaseApp.StartAsync(WithStatusCodePages(options => options.ExcludePaths("/error/**")));

        var response = await app.GetAsync("/missing");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.Contains(PathBaseApp.TraceIdHeader).ShouldBeTrue();
        var stored = await app.StoredAsync(response);
        stored.Trace.Method.ShouldBe("GET");
        stored.Trace.HttpStatus.ShouldBe(404);
        app.StartedTraces.Count.ShouldBe(1);
    }

    [Fact]
    public async Task StatusCodePageReExecution_ForAnUnexcludedPath_RecordsTheRequestOnce()
    {
        await using var app = await PathBaseApp.StartAsync(WithStatusCodePages(_ => { }));

        var response = await app.GetAsync("/missing");

        response.Headers.Contains(PathBaseApp.TraceIdHeader).ShouldBeTrue();
        (await app.StoredAsync(response)).Trace.HttpStatus.ShouldBe(404);
        app.StartedTraces.Count.ShouldBe(1);
        app.Recorder.ActiveTraceCount.ShouldBe(0);
    }

    [Fact]
    public async Task ExceptionHandlerReExecution_ForAPathInExcludePaths_LeavesNoTraceAndNoHeader()
    {
        await using var app = await PathBaseApp.StartAsync(WithExceptionHandler(options => options.ExcludePaths("/internal/**")));

        var response = await app.GetAsync("/internal/crash");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        await ShouldHaveLeftNoTraceAsync(app, response);
    }

    [Fact]
    public async Task ExceptionHandlerReExecution_WhenOnlyTheErrorPathIsExcluded_StillRecordsTheRequest()
    {
        await using var app = await PathBaseApp.StartAsync(WithExceptionHandler(options => options.ExcludePaths("/error")));

        var response = await app.GetAsync("/crash");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.Contains(PathBaseApp.TraceIdHeader).ShouldBeTrue();
        (await app.StoredAsync(response)).Trace.Status.ShouldBe(FlowStatus.Error);
        app.StartedTraces.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExceptionHandlerReExecution_ForAnUnexcludedPath_RecordsTheRequestOnce()
    {
        await using var app = await PathBaseApp.StartAsync(WithExceptionHandler(_ => { }));

        var response = await app.GetAsync("/crash");

        response.Headers.Contains(PathBaseApp.TraceIdHeader).ShouldBeTrue();
        (await app.StoredAsync(response)).Trace.Status.ShouldBe(FlowStatus.Error);
        app.StartedTraces.Count.ShouldBe(1);
        app.Recorder.ActiveTraceCount.ShouldBe(0);
    }

    [Fact]
    public async Task ExceptionHandlerReExecution_AfterAnEndpointExcludedByItsPathFailed_LeavesNoTrace()
    {
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            Configure = options => options.ExcludePaths("/health"),
            BeforeRouting = builder => builder.UseExceptionHandler("/error"),
            Map = endpoints =>
            {
                endpoints.MapGet("/health", () => Task.FromException<string>(new InvalidOperationException("The check failed.")));
                endpoints.MapGet("/error", () => "error page");
            },
        });

        var response = await app.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        await ShouldHaveLeftNoTraceAsync(app, response);
    }
}
