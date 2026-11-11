using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using NetApiFlow.EntityFrameworkCore;
using NetApiFlow.Marking;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;
using Sample = SampleApi.Controllers;

namespace NetApiFlow.IntegrationTests;

/// <summary>
/// Verifies Authorization and Resource filter steps against the Controllers sample. Neither stage
/// is exercised by the sample's own <c>Program.cs</c> (no <c>[Authorize]</c>, no resource filter
/// configured), so a test-local Authorization filter stands in for one instead of touching the
/// shared sample.
/// </summary>
public class PrePipelineStepsIntegrationTests
{
    private static (IHost Host, SqliteConnection Connection) CreateHost(
        bool capturePrePipelineSteps,
        IFilterMetadata? authorizationFilter = null,
        int exceptionHandlerStatusCode = StatusCodes.Status500InternalServerError)
    {
        // A shared-cache in-memory database (one connection string, a separate physical
        // connection per DbContext) instead of a single shared connection: genuine concurrent
        // requests each opening their own connection need to read without contending for one.
        var connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var connection = new SqliteConnection(connectionString);
        connection.Open();

        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddControllers().AddApplicationPart(typeof(Sample.Controllers.OrdersController).Assembly);
                    services.AddHttpClient();

                    services.AddNetApiFlow(o =>
                    {
                        o.IncludePaths("/api/**");
                        o.CapturePrePipelineSteps = capturePrePipelineSteps;
                    });
                    services.AddNetApiFlowEntityFrameworkCore();

                    if (authorizationFilter is not null)
                    {
                        services.Configure<MvcOptions>(options => options.Filters.Add(authorizationFilter));
                    }

                    services.AddDbContext<Sample.Data.SampleDbContext>((sp, options) =>
                    {
                        options.UseSqlite(connectionString);
                        options.AddInterceptors(sp.GetRequiredService<FlowDbCommandInterceptor>());
                    });

                    services.AddFlowTracked<Sample.Data.IOrderRepository, Sample.Data.OrderRepository>(ComponentType.Repository);
                    services.AddFlowTracked<Sample.Application.IOrderService, Sample.Application.OrderService>(ComponentType.Service);
                });
                webBuilder.Configure(app =>
                {
                    app.UseExceptionHandler(exceptionApp => exceptionApp.Run(
                        context =>
                        {
                            context.Response.StatusCode = exceptionHandlerStatusCode;
                            return Task.CompletedTask;
                        }));
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .Build();

        host.Start();

        using (var scope = host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Sample.Data.SampleDbContext>();
            dbContext.Database.EnsureCreated();
            dbContext.Orders.AddRange(
                new Sample.Data.Order { Id = 1, CustomerName = "Ada Lovelace", Amount = 120.50m, Status = "Pending" },
                new Sample.Data.Order { Id = 2, CustomerName = "Alan Turing", Amount = 75.00m, Status = "Shipped" });
            dbContext.SaveChanges();
        }

        return (host, connection);
    }

    private static ConcurrentDictionary<string, List<FlowSpan>> TrackSpans(FlowRecorder recorder)
    {
        var spansByTrace = new ConcurrentDictionary<string, List<FlowSpan>>();
        void OnSpan(FlowSpan span) => spansByTrace.GetOrAdd(span.TraceId, _ => []).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;
        return spansByTrace;
    }

    [Fact]
    public async Task Get_WithCapturePrePipelineStepsEnabled_ProducesAuthorizationAndResourceFilterSpansNested()
    {
        var (host, connection) = CreateHost(capturePrePipelineSteps: true, authorizationFilter: new AllowAllAuthorizationFilter());
        using var _ = host;
        using var __ = connection;

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = TrackSpans(recorder);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/orders/1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var spans = spansByTrace[traceId];

        var authorizationSpan = spans.Single(s => s.Component == "AuthorizationFilters");
        var resourceSpan = spans.Single(s => s.Component == "ResourceFilters");
        var controllerSpan = spans.Single(s => s.Type == ComponentType.Controller);

        authorizationSpan.Type.ShouldBe(ComponentType.Service);
        authorizationSpan.ParentSpanId.ShouldBeNull();
        authorizationSpan.Status.ShouldBe(FlowStatus.Success);

        resourceSpan.Type.ShouldBe(ComponentType.Service);
        resourceSpan.ParentSpanId.ShouldBeNull();
        resourceSpan.Status.ShouldBe(FlowStatus.Success);

        controllerSpan.ParentSpanId.ShouldBe(resourceSpan.SpanId);
    }

    [Fact]
    public async Task Get_WithCapturePrePipelineStepsDisabled_ProducesNoAuthorizationOrResourceFilterSpans()
    {
        var (host, connection) = CreateHost(capturePrePipelineSteps: false, authorizationFilter: new AllowAllAuthorizationFilter());
        using var _ = host;
        using var __ = connection;

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = TrackSpans(recorder);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/orders/1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var spans = spansByTrace[traceId];

        spans.ShouldNotContain(s => s.Component == "AuthorizationFilters");
        spans.ShouldNotContain(s => s.Component == "ResourceFilters");
        spans.ShouldContain(s => s.Type == ComponentType.Controller);
    }

    [Fact]
    public async Task Get_WithAuthorizationFilterTagCuration_NeverCopiesAuthorizationContextFields()
    {
        var (host, connection) = CreateHost(capturePrePipelineSteps: true, authorizationFilter: new AllowAllAuthorizationFilter());
        using var _ = host;
        using var __ = connection;

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = TrackSpans(recorder);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/orders/1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var authorizationSpan = spansByTrace[traceId].Single(s => s.Component == "AuthorizationFilters");

        authorizationSpan.Operation.ShouldBe(nameof(AllowAllAuthorizationFilter));
        authorizationSpan.Tags.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_BusinessErrorScenario_MarksResourceFiltersSpanAsErrorButNotAuthorizationFilters()
    {
        var (host, connection) = CreateHost(capturePrePipelineSteps: true, authorizationFilter: new AllowAllAuthorizationFilter());
        using var _ = host;
        using var __ = connection;

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = TrackSpans(recorder);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/orders/2/confirm");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var spans = spansByTrace[traceId];

        var authorizationSpan = spans.Single(s => s.Component == "AuthorizationFilters");
        var resourceSpan = spans.Single(s => s.Component == "ResourceFilters");

        authorizationSpan.Status.ShouldBe(FlowStatus.Success);
        authorizationSpan.IsErrorOrigin.ShouldBeFalse();

        resourceSpan.Status.ShouldBe(FlowStatus.Error);
        resourceSpan.IsErrorOrigin.ShouldBeFalse();
    }

    [Fact]
    public async Task Get_WithThrowingAuthorizationFilter_MarksAuthorizationFiltersSpanAsError()
    {
        var (host, connection) = CreateHost(capturePrePipelineSteps: true, authorizationFilter: new ThrowingAuthorizationFilter());
        using var _ = host;
        using var __ = connection;

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = TrackSpans(recorder);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/orders/1");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var spans = spansByTrace[traceId];

        var authorizationSpan = spans.Single(s => s.Component == "AuthorizationFilters");
        authorizationSpan.Status.ShouldBe(FlowStatus.Error);
        authorizationSpan.ErrorType.ShouldBe(typeof(InvalidOperationException).FullName);
        authorizationSpan.IsErrorOrigin.ShouldBeTrue();

        spans.ShouldNotContain(s => s.Component == "ResourceFilters");
    }

    [Fact]
    public async Task Get_WithThrowingAuthorizationFilterHandledAsClientError_MarksSpanErrorButNotTheWholeTrace()
    {
        var (host, connection) = CreateHost(
            capturePrePipelineSteps: true,
            authorizationFilter: new ThrowingAuthorizationFilter(),
            exceptionHandlerStatusCode: StatusCodes.Status400BadRequest);
        using var _ = host;
        using var __ = connection;

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = TrackSpans(recorder);
        FlowTrace? completedTrace = null;
        recorder.TraceCompleted += trace => completedTrace = trace;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/orders/1");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var authorizationSpan = spansByTrace[traceId].Single(s => s.Component == "AuthorizationFilters");

        authorizationSpan.Status.ShouldBe(FlowStatus.Error);

        completedTrace.ShouldNotBeNull();
        completedTrace.Status.ShouldBe(FlowStatus.Success);
        completedTrace.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Get_ConcurrentRequestsSharingOneAuthorizationFilterInstance_EachClosesItsOwnStepOnly()
    {
        const int concurrentRequestCount = 30;

        var (host, connection) = CreateHost(capturePrePipelineSteps: true, authorizationFilter: new DelayingAuthorizationFilter());
        using var _ = host;
        using var __ = connection;

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = TrackSpans(recorder);

        var client = host.GetTestServer().CreateClient();
        var traceIds = Enumerable.Range(0, concurrentRequestCount)
            .Select(_ => Guid.NewGuid().ToString("N"))
            .ToList();

        var responses = await Task.WhenAll(traceIds.Select(traceId => SendWithTraceParentAsync(client, "/api/orders/1", traceId)));
        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);

        foreach (var traceId in traceIds)
        {
            var authorizationSpans = spansByTrace[traceId].Where(s => s.Component == "AuthorizationFilters").ToList();
            authorizationSpans.Count.ShouldBe(1);
            authorizationSpans[0].Status.ShouldBe(FlowStatus.Success);
            authorizationSpans[0].TraceId.ShouldBe(traceId);
        }
    }

    private static async Task<HttpResponseMessage> SendWithTraceParentAsync(HttpClient client, string path, string traceId)
    {
        var spanId = Guid.NewGuid().ToString("N")[..16];
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("traceparent", $"00-{traceId}-{spanId}-01");
        return await client.SendAsync(request);
    }

    private sealed class AllowAllAuthorizationFilter : IAsyncAuthorizationFilter
    {
        public Task OnAuthorizationAsync(AuthorizationFilterContext context) => Task.CompletedTask;
    }

    private sealed class ThrowingAuthorizationFilter : IAsyncAuthorizationFilter
    {
        public Task OnAuthorizationAsync(AuthorizationFilterContext context) =>
            throw new InvalidOperationException("Spike probe: Authorization filter failing on purpose.");
    }

    private sealed class DelayingAuthorizationFilter : IAsyncAuthorizationFilter
    {
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context) =>
            await Task.Delay(TimeSpan.FromMilliseconds(5));
    }
}
