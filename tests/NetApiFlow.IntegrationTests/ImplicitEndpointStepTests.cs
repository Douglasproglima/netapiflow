using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public sealed class ImplicitEndpointStepTests
{
    private static string Hello() => "hello";

    [Fact]
    public async Task MinimalApiEndpoint_WithoutMarking_RecordsOneEndpointStepNamedLikeTheFilterNamesIt()
    {
        await using var app = await TestApp.StartAsync(map: endpoints =>
        {
            endpoints.MapGet("/unmarked", Hello);
            endpoints.MapGet("/marked", Hello).WithNetApiFlowTracking();
        });

        var unmarked = await app.TraceOfAsync("/unmarked");
        var marked = await app.TraceOfAsync("/marked");

        var step = unmarked.Spans.ShouldHaveSingleItem();
        step.Type.ShouldBe(ComponentType.Endpoint);
        step.Component.ShouldBe(nameof(ImplicitEndpointStepTests));
        step.Operation.ShouldBe(nameof(Hello));
        marked.Spans.Single().Component.ShouldBe(step.Component);
        marked.Spans.Single().Operation.ShouldBe(step.Operation);
    }

    [Fact]
    public async Task MinimalApiEndpoint_WithAnAnonymousHandler_IsNamedFromItsVerbsAndRoute()
    {
        await using var app = await TestApp.StartAsync(map: endpoints => endpoints.MapGet("/hello", () => "hi"));

        var trace = await app.TraceOfAsync("/hello");

        var step = trace.Spans.ShouldHaveSingleItem();
        step.Component.ShouldBe("Minimal API");
        step.Operation.ShouldBe("GET /hello");
    }

    [Fact]
    public async Task MinimalApiEndpoint_AlsoMarkedWithTheFilter_StillRecordsOneStep()
    {
        await using var app = await TestApp.StartAsync(map: endpoints => endpoints.MapGet("/both", () => "hi").WithNetApiFlowTracking());

        var trace = await app.TraceOfAsync("/both");

        trace.Spans.Count(span => span.Type == ComponentType.Endpoint).ShouldBe(1);
    }

    [Fact]
    public async Task MvcAction_RecordsTheControllerStepAndNoEndpointStep()
    {
        await using var app = await TestApp.StartAsync(controllers: true, map: endpoints => endpoints.MapControllers());

        var trace = await app.TraceOfAsync("/api/orders/7");

        trace.Spans.ShouldContain(span => span.Type == ComponentType.Controller);
        trace.Spans.ShouldNotContain(span => span.Type == ComponentType.Endpoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MinimalApiEndpoint_WithTheOptionOff_RecordsNoStepUnlessMarked(bool offOnlyInConfiguration)
    {
        await using var app = await TestApp.StartAsync(
            configure: offOnlyInConfiguration ? null : options => options.CaptureMinimalApiEndpoints = false,
            configuration: offOnlyInConfiguration ? new Dictionary<string, string?> { ["NetApiFlow:CaptureMinimalApiEndpoints"] = "false" } : null,
            map: endpoints =>
            {
                endpoints.MapGet("/unmarked", () => "hi");
                endpoints.MapGet("/marked", () => "hi").WithNetApiFlowTracking();
            });

        var unmarked = await app.TraceOfAsync("/unmarked");
        var marked = await app.TraceOfAsync("/marked");

        unmarked.Spans.ShouldBeEmpty();
        marked.Spans.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task TwoHostsInTheSameProcess_EachDecidesByItsOwnOption()
    {
        await using var off = await TestApp.StartAsync(
            configure: options => options.CaptureMinimalApiEndpoints = false,
            map: endpoints => endpoints.MapGet("/hello", () => "off"));
        await using var on = await TestApp.StartAsync(map: endpoints => endpoints.MapGet("/hello", () => "on"));

        var fromTheHostWithTheOptionOff = await off.TraceOfAsync("/hello");
        var fromTheHostWithTheOptionOn = await on.TraceOfAsync("/hello");

        fromTheHostWithTheOptionOff.Spans.ShouldBeEmpty();
        fromTheHostWithTheOptionOn.Spans.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task MinimalApiEndpoint_InsideAGroupMarkedWithTheFilter_RecordsOneStep()
    {
        await using var app = await TestApp.StartAsync(map: endpoints =>
            endpoints.MapGroup("/group").WithNetApiFlowTracking().MapGet("/item", () => "hi"));

        var trace = await app.TraceOfAsync("/group/item");

        trace.Spans.Count(span => span.Type == ComponentType.Endpoint).ShouldBe(1);
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public async Task MinimalApiEndpoint_ThatThrows_RecordsTheEndpointStepAsTheOriginOfTheError(string environment, bool useExceptionHandler)
    {
        await using var app = await TestApp.StartAsync(
            environment: environment,
            beforeRouting: pipeline =>
            {
                if (useExceptionHandler)
                {
                    pipeline.UseExceptionHandler(errorApp => errorApp.Run(context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        return Task.CompletedTask;
                    }));
                }
                else if (environment == "Development")
                {
                    pipeline.UseDeveloperExceptionPage();
                }
            },
            map: endpoints => endpoints.MapGet("/boom", string () => throw new InvalidOperationException("boom")));

        var trace = await app.TraceOfAsync("/boom", tolerateAnUnhandledException: true);

        var step = trace.Spans.ShouldHaveSingleItem();
        step.Type.ShouldBe(ComponentType.Endpoint);
        step.Status.ShouldBe(FlowStatus.Error);
        step.IsErrorOrigin.ShouldBeTrue();
        step.ErrorType.ShouldBe(typeof(InvalidOperationException).FullName);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/json")]
    [InlineData("*/*")]
    [InlineData(null)]
    public async Task MinimalApiEndpoint_ThatThrowsUnderTheDeveloperPage_RecordsTheErrorWhateverTheClientAccepts(string? accept)
    {
        await using var app = await TestApp.StartAsync(
            environment: "Development",
            beforeRouting: pipeline => pipeline.UseDeveloperExceptionPage(),
            map: endpoints => endpoints.MapGet("/boom", string () => throw new InvalidOperationException("boom")));

        var trace = await app.TraceOfAsync("/boom", accept: accept);

        var step = trace.Spans.ShouldHaveSingleItem();
        step.Status.ShouldBe(FlowStatus.Error);
        step.IsErrorOrigin.ShouldBeTrue();
        step.ErrorType.ShouldBe(typeof(InvalidOperationException).FullName);
        step.ErrorMessage.ShouldBe("boom");
    }

    [Fact]
    public async Task MinimalApiEndpoint_ThatAnswers500WithoutAnException_RecordsTheStepAsFailedWithoutAType()
    {
        await using var app = await TestApp.StartAsync(map: endpoints => endpoints.MapGet("/five-hundred", () => Results.StatusCode(StatusCodes.Status500InternalServerError)));

        var trace = await app.TraceOfAsync("/five-hundred");

        var step = trace.Spans.ShouldHaveSingleItem();
        step.Status.ShouldBe(FlowStatus.Error);
        step.IsErrorOrigin.ShouldBeTrue();
        step.ErrorType.ShouldBeNull();
    }

    [Theory]
    [InlineData("/flow/anything", false, false)]
    [InlineData("/openapi/v1.json", false, false)]
    [InlineData("/excluded/thing", false, false)]
    [InlineData("/hello", true, false)]
    [InlineData("/flow/anything", false, true)]
    [InlineData("/openapi/v1.json", false, true)]
    [InlineData("/excluded/thing", false, true)]
    [InlineData("/hello", true, true)]
    public async Task RequestTheProductDoesNotTrace_NeverGetsATraceOrAnEndpointStep(string path, bool disabled, bool withAnotherListenerOnTheSource)
    {
        await using var app = await TestApp.StartAsync(
            configure: options =>
            {
                options.ExcludePaths("/excluded/**");
                options.Enabled = !disabled;
            },
            withAnotherListenerOnTheSource: withAnotherListenerOnTheSource,
            map: endpoints =>
            {
                endpoints.MapGet("/flow/anything", () => "x");
                endpoints.MapGet("/openapi/v1.json", () => "x");
                endpoints.MapGet("/excluded/thing", () => "x");
                endpoints.MapGet("/hello", () => "x");
            });

        await app.RequestAndWaitForTheEndAsync(path);

        app.CompletedTraces.ShouldBeEmpty();
        app.StartedActivities.ShouldBeEmpty();
    }

    [Fact]
    public async Task TrackedRequest_WithAnotherListenerOnTheSource_StillOpensTheEndpointActivity()
    {
        await using var app = await TestApp.StartAsync(
            withAnotherListenerOnTheSource: true,
            map: endpoints => endpoints.MapGet("/hello", () => "x"));

        await app.RequestAndWaitForTheEndAsync("/hello");

        app.StartedActivities.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task SixteenConcurrentRequests_EachGetsItsOwnTraceWithOneEndpointStep()
    {
        await using var app = await TestApp.StartAsync(map: endpoints => endpoints.MapGet("/work", async () =>
        {
            await Task.Delay(20);
            return "done";
        }));

        var traces = await app.TracesOfAsync(Enumerable.Repeat("/work", 16).ToList());

        traces.Count.ShouldBe(16);
        traces.Select(trace => trace.Trace.TraceId).Distinct().Count().ShouldBe(16);
        foreach (var trace in traces)
        {
            var step = trace.Spans.ShouldHaveSingleItem();
            step.Type.ShouldBe(ComponentType.Endpoint);
            step.TraceId.ShouldBe(trace.Trace.TraceId);
        }
    }

    [Fact]
    public async Task StepOpenedInsideTheHandler_HasTheEndpointStepAsItsParent()
    {
        await using var app = await TestApp.StartAsync(map: endpoints => endpoints.MapGet("/orders", () =>
        {
            using var repository = NetApiFlow.Step("OrderRepository", "FindAsync", ComponentType.Repository);
            return "ok";
        }));

        var trace = await app.TraceOfAsync("/orders");

        var endpoint = trace.Spans.Single(span => span.Type == ComponentType.Endpoint);
        var repository = trace.Spans.Single(span => span.Type == ComponentType.Repository);
        repository.ParentSpanId.ShouldBe(endpoint.SpanId);
    }

    [Fact]
    public async Task HealthChecksAndStaticFiles_NeverGetAnEndpointStep()
    {
        var staticFile = $"{typeof(ImplicitEndpointStepTests).Assembly.GetName().Name}.deps.json";
        await using var app = await TestApp.StartAsync(
            services: services => services.AddHealthChecks(),
            beforeRouting: pipeline => pipeline.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(AppContext.BaseDirectory) }),
            map: endpoints => endpoints.MapHealthChecks("/health"));

        var health = await app.TraceOfAsync("/health");
        var file = await app.TraceOfAsync($"/{staticFile}");

        health.Spans.ShouldNotContain(span => span.Type == ComponentType.Endpoint);
        file.Spans.ShouldBeEmpty();
    }

    private sealed class TestApp : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly ActivityListener? _anotherListener;
        private readonly ConcurrentQueue<FlowTrace> _completedTraces = new();
        private readonly ConcurrentQueue<string> _startedActivities = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _requestEnds = new();

        private TestApp(IHost host, bool withAnotherListenerOnTheSource)
        {
            _host = host;
            host.Services.GetRequiredService<FlowRecorder>().TraceCompleted += _completedTraces.Enqueue;
            if (withAnotherListenerOnTheSource)
            {
                _anotherListener = new ActivityListener
                {
                    ShouldListenTo = source => source.Name == FlowActivitySource.Name,
                    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStarted = activity => _startedActivities.Enqueue(activity.OperationName),
                };
                ActivitySource.AddActivityListener(_anotherListener);
            }
        }

        public IReadOnlyCollection<FlowTrace> CompletedTraces => _completedTraces.ToArray();

        public IReadOnlyCollection<string> StartedActivities => _startedActivities.ToArray();

        public static async Task<TestApp> StartAsync(
            Action<NetApiFlowOptions>? configure = null,
            Dictionary<string, string?>? configuration = null,
            string environment = "Production",
            bool controllers = false,
            bool withAnotherListenerOnTheSource = false,
            Action<IServiceCollection>? services = null,
            Action<IApplicationBuilder>? beforeRouting = null,
            Action<IEndpointRouteBuilder>? map = null)
        {
            TestApp? created = null;
            var host = await Host.CreateDefaultBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.UseEnvironment(environment);
                    if (configuration is not null)
                    {
                        webBuilder.ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(configuration));
                    }

                    webBuilder.ConfigureServices(serviceCollection =>
                    {
                        serviceCollection.AddRouting();
                        if (controllers)
                        {
                            serviceCollection.AddControllers().AddApplicationPart(typeof(OrdersController).Assembly);
                        }

                        serviceCollection.AddNetApiFlow(configure);
                        services?.Invoke(serviceCollection);
                    });
                    webBuilder.Configure(pipeline =>
                    {
                        pipeline.Use((context, next) =>
                        {
                            context.Response.OnCompleted(() =>
                            {
                                created?.SignalRequestEnd(context.Request.Path);
                                return Task.CompletedTask;
                            });
                            return next(context);
                        });
                        beforeRouting?.Invoke(pipeline);
                        pipeline.UseRouting();
                        pipeline.UseEndpoints(endpoints => map?.Invoke(endpoints));
                    });
                })
                .StartAsync();

            created = new TestApp(host, withAnotherListenerOnTheSource);
            return created;
        }

        public async Task RequestAndWaitForTheEndAsync(string path)
        {
            var ended = _requestEnds.GetOrAdd(path, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            using var response = await _host.GetTestServer().CreateClient().GetAsync(path);
            await ended.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }

        public async Task<FlowTraceDetail> TraceOfAsync(string path, bool tolerateAnUnhandledException = false, string? accept = null)
        {
            var before = _completedTraces.Count;
            var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _requestEnds[path] = ended;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                if (accept is not null)
                {
                    request.Headers.TryAddWithoutValidation("Accept", accept);
                }

                using var response = await _host.GetTestServer().CreateClient().SendAsync(request);
            }
            catch (InvalidOperationException) when (tolerateAnUnhandledException)
            {
                ended.TrySetResult();
            }

            await ended.Task.WaitAsync(TimeSpan.FromSeconds(30));
            return DetailOf(_completedTraces.ElementAt(before));
        }

        public async Task<List<FlowTraceDetail>> TracesOfAsync(List<string> paths)
        {
            var before = _completedTraces.Count;
            await Task.WhenAll(paths.Select(async path =>
            {
                using var response = await _host.GetTestServer().CreateClient().GetAsync(path);
            }));
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (_completedTraces.Count - before < paths.Count && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            return _completedTraces.Skip(before).Select(DetailOf).ToList();
        }

        public async ValueTask DisposeAsync()
        {
            _anotherListener?.Dispose();
            await _host.StopAsync();
            _host.Dispose();
        }

        private FlowTraceDetail DetailOf(FlowTrace trace) =>
            NetApiFlow.GetRecentTrace(_host.Services, trace.TraceId) ?? throw new InvalidOperationException($"Trace {trace.TraceId} is not in the recent buffer.");

        private void SignalRequestEnd(PathString path)
        {
            if (_requestEnds.TryGetValue(path.Value ?? string.Empty, out var ended))
            {
                ended.TrySetResult();
            }
        }
    }
}
