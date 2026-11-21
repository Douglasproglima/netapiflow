using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class UnhandledExceptionTraceIdHeaderTests
{
    private const string TraceIdHeader = "X-NetApiFlow-TraceId";
    private const string ThrowingRoute = "/api/boom";
    private const string WorkingRoute = "/api/ok";

    [Fact]
    public async Task UnhandledException_InDevelopmentWithTheHeaderOn_IsAnswered500CarryingTheTraceIdOfItsTrace()
    {
        await using var app = await RunningApp.StartAsync(Environments.Development, emitTraceIdHeader: true);

        var response = await app.Client.GetAsync(ThrowingRoute);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.TryGetValues(TraceIdHeader, out var values).ShouldBeTrue();
        var trace = await app.CompletedTraceAsync();
        trace.TraceId.ShouldBe(values!.Single());
        trace.Status.ShouldBe(FlowStatus.Error);
        trace.HttpStatus.ShouldBe(500);
    }

    [Fact]
    public async Task UnhandledException_InProductionWithAnExceptionHandler_IsAnswered500CarryingTheTraceIdOfItsTrace()
    {
        await using var app = await RunningApp.StartAsync(Environments.Production, emitTraceIdHeader: true, useExceptionHandler: true);

        var response = await app.Client.GetAsync(ThrowingRoute);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.TryGetValues(TraceIdHeader, out var values).ShouldBeTrue();
        (await app.CompletedTraceAsync()).TraceId.ShouldBe(values!.Single());
    }

    [Fact]
    public async Task UnhandledException_InProductionWithoutAnExceptionHandler_KnownLimitTheServerBuilt500CarriesNoHeader()
    {
        await using var app = await RunningApp.StartAsync(Environments.Production, emitTraceIdHeader: true);

        var response = await app.Client.GetAsync(ThrowingRoute);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.Contains(TraceIdHeader).ShouldBeFalse();
        response.Headers.Server.ToString().ShouldBe("Kestrel");
        response.Content.Headers.ContentLength.ShouldBe(0);
        (await app.CompletedTraceAsync()).Status.ShouldBe(FlowStatus.Error);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task UnhandledException_WithTheHeaderOff_IsAnswered500CarryingNoHeader(string environment)
    {
        await using var app = await RunningApp.StartAsync(environment, emitTraceIdHeader: false);

        var response = await app.Client.GetAsync(ThrowingRoute);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.Contains(TraceIdHeader).ShouldBeFalse();
        (await app.CompletedTraceAsync()).Status.ShouldBe(FlowStatus.Error);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Request_ThatSucceeds_StillCarriesTheTraceIdHeader(string environment)
    {
        await using var app = await RunningApp.StartAsync(environment, emitTraceIdHeader: true);

        var response = await app.Client.GetAsync(WorkingRoute);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.TryGetValues(TraceIdHeader, out var values).ShouldBeTrue();
        values!.Single().ShouldBe((await app.CompletedTraceAsync()).TraceId);
    }

    private sealed class RunningApp : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly TaskCompletionSource<FlowTrace> _completed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private RunningApp(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
            app.Services.GetRequiredService<FlowRecorder>().TraceCompleted += trace => _completed.TrySetResult(trace);
        }

        public HttpClient Client { get; }

        public static async Task<RunningApp> StartAsync(string environment, bool emitTraceIdHeader, bool useExceptionHandler = false)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddNetApiFlow(options => options.EmitTraceIdHeader = emitTraceIdHeader);

            var app = builder.Build();
            if (useExceptionHandler)
            {
                app.UseExceptionHandler(errorApp => errorApp.Run(context =>
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    return Task.CompletedTask;
                }));
            }

            app.MapGet(WorkingRoute, () => "ok");
            app.MapGet(ThrowingRoute, () => Throw());
            await app.StartAsync();

            return new RunningApp(app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()) });
        }

        public async Task<FlowTrace> CompletedTraceAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            return await _completed.Task.WaitAsync(timeout.Token);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }

    private static string Throw() => throw new InvalidOperationException("boom");
}
