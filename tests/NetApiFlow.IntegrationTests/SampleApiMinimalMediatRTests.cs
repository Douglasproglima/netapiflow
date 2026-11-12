using System.Collections.Concurrent;
using System.Net;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using NetApiFlow.EntityFrameworkCore;
using NetApiFlow.Marking;
using NetApiFlow.MediatR;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;
using Sample = SampleApi.MinimalMediatR;

namespace NetApiFlow.IntegrationTests;

/// <summary>
/// Verifies the MinimalMediatR sample produces the full
/// Endpoint → UseCase → Service → Repository → Database flow automatically, with no manual
/// <c>NetApiFlow.Step(...)</c> call anywhere in the sample's business code.
/// </summary>
public class SampleApiMinimalMediatRTests
{
    private static (IHost Host, SqliteConnection Connection) CreateHost()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();

                    services.AddNetApiFlow(o => o.IncludePaths("/api/**"));
                    services.AddNetApiFlowEntityFrameworkCore();

                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Sample.Application.GetOrderQuery>());
                    services.AddNetApiFlowMediatR();

                    services.AddDbContext<Sample.Data.SampleDbContext>((sp, options) =>
                    {
                        options.UseSqlite(connection);
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
                            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                            return Task.CompletedTask;
                        }));
                    app.UseMiddleware<Sample.DemoDelayMiddleware>();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        var orders = endpoints.MapGroup("/api/orders").WithNetApiFlowTracking();

                        orders.MapGet("/{id:int}", async (int id, IMediator mediator, CancellationToken cancellationToken) =>
                        {
                            var order = await mediator.Send(new Sample.Application.GetOrderQuery(id), cancellationToken);
                            return order is null ? Results.NotFound() : Results.Ok(order);
                        });

                        orders.MapGet("/{id:int}/confirm", async (int id, IMediator mediator, CancellationToken cancellationToken) =>
                            Results.Ok(await mediator.Send(new Sample.Application.ConfirmOrderCommand(id), cancellationToken)));

                        orders.MapGet("/{id:int}/slow", async (int id, IMediator mediator, CancellationToken cancellationToken) =>
                        {
                            var order = await mediator.Send(new Sample.Application.GetOrderSlowlyQuery(id), cancellationToken);
                            return order is null ? Results.NotFound() : Results.Ok(order);
                        });

                        orders.MapGet("/parallel", async (int[] ids, IMediator mediator, CancellationToken cancellationToken) =>
                            Results.Ok(await mediator.Send(new Sample.Application.GetOrdersParallelQuery(ids), cancellationToken)));

                        orders.MapGet("/sequential", async (int[] ids, IMediator mediator, CancellationToken cancellationToken) =>
                            Results.Ok(await mediator.Send(new Sample.Application.GetOrdersSequentiallyQuery(ids), cancellationToken)));
                    });
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
                new Sample.Data.Order { Id = 2, CustomerName = "Alan Turing", Amount = 75.00m, Status = "Shipped" },
                new Sample.Data.Order { Id = 3, CustomerName = "Grace Hopper", Amount = 42.00m, Status = "Pending" });
            dbContext.SaveChanges();
        }

        return (host, connection);
    }

    [Fact]
    public async Task Get_SuccessScenario_ProducesEndpointUseCaseServiceRepositoryDatabaseSpans()
    {
        var (host, connection) = CreateHost();
        using var _ = host;
        using var __ = connection;

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = new ConcurrentDictionary<string, List<FlowSpan>>();
        void OnSpan(FlowSpan span) => spansByTrace.GetOrAdd(span.TraceId, _ => []).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/orders/1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var spans = spansByTrace[traceId];

        spans.ShouldContain(s => s.Type == ComponentType.Endpoint);
        spans.ShouldContain(s => s.Type == ComponentType.UseCase && s.Component == nameof(Sample.Application.GetOrderQuery));
        spans.ShouldContain(s => s.Type == ComponentType.Service && s.Component == "OrderService");
        spans.ShouldContain(s => s.Type == ComponentType.Repository && s.Component == "OrderRepository");
        spans.ShouldContain(s => s.Type == ComponentType.Database);
        spans.ShouldAllBe(s => s.Status == FlowStatus.Success);
    }

    [Fact]
    public async Task Get_BusinessErrorScenario_MarksDescendantSpansAsError()
    {
        var (host, connection) = CreateHost();
        using var _ = host;
        using var __ = connection;

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = new ConcurrentDictionary<string, List<FlowSpan>>();
        void OnSpan(FlowSpan span) => spansByTrace.GetOrAdd(span.TraceId, _ => []).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;

        var client = host.GetTestServer().CreateClient();
        // Order 2 is seeded as "Shipped", so the business rule (must be Pending) fails mid-flow.
        var response = await client.GetAsync("/api/orders/2/confirm");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var spans = spansByTrace[traceId];

        var useCaseSpan = spans.Single(s => s.Type == ComponentType.UseCase);
        useCaseSpan.Status.ShouldBe(FlowStatus.Error);
        useCaseSpan.IsErrorOrigin.ShouldBeFalse();

        var serviceSpan = spans.Single(s => s.Type == ComponentType.Service);
        serviceSpan.Status.ShouldBe(FlowStatus.Error);
        serviceSpan.IsErrorOrigin.ShouldBeTrue();

        var endpointSpan = spans.Single(s => s.Type == ComponentType.Endpoint);
        endpointSpan.Status.ShouldBe(FlowStatus.Error);
        endpointSpan.IsErrorOrigin.ShouldBeFalse();
    }

    [Fact]
    public async Task Get_SlowScenario_DurationReflectsTheArtificialDelay()
    {
        var (host, connection) = CreateHost();
        using var _ = host;
        using var __ = connection;

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = new ConcurrentDictionary<string, List<FlowSpan>>();
        void OnSpan(FlowSpan span) => spansByTrace.GetOrAdd(span.TraceId, _ => []).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/orders/1/slow");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var useCaseSpan = spansByTrace[traceId].Single(s => s.Type == ComponentType.UseCase);

        useCaseSpan.DurationMicros!.Value.ShouldBeGreaterThanOrEqualTo(250_000);
    }

    [Fact]
    public async Task Get_ParallelScenario_ProducesOneRepositorySpanPerConcurrentCall()
    {
        var (host, connection) = CreateHost();
        using var _ = host;
        using var __ = connection;

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = new ConcurrentDictionary<string, List<FlowSpan>>();
        void OnSpan(FlowSpan span) => spansByTrace.GetOrAdd(span.TraceId, _ => []).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/orders/parallel?ids=1&ids=2&ids=3");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var spans = spansByTrace[traceId];

        var serviceSpan = spans.Single(s => s.Type == ComponentType.Service);
        var repositorySpans = spans.Where(s => s.Type == ComponentType.Repository).ToList();

        repositorySpans.Count.ShouldBe(3);
        repositorySpans.ShouldAllBe(s => s.ParentSpanId == serviceSpan.SpanId);
    }

    [Fact]
    public async Task Get_SequentialScenario_ProducesOneDatabaseSpanPerIdUnderASingleRepositoryCall()
    {
        var (host, connection) = CreateHost();
        using var _ = host;
        using var __ = connection;

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = new ConcurrentDictionary<string, List<FlowSpan>>();
        void OnSpan(FlowSpan span) => spansByTrace.GetOrAdd(span.TraceId, _ => []).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/api/orders/sequential?ids=1&ids=2&ids=3");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var spans = spansByTrace[traceId];

        var repositorySpan = spans.Single(s => s.Type == ComponentType.Repository);
        var databaseSpans = spans.Where(s => s.Type == ComponentType.Database).ToList();

        databaseSpans.Count.ShouldBe(3);
        databaseSpans.ShouldAllBe(s => s.ParentSpanId == repositorySpan.SpanId);
    }

    [Fact]
    public async Task Get_WithDemoDelayHeader_IncludesTheDelayInTraceDuration()
    {
        var (host, connection) = CreateHost();
        using var _ = host;
        using var __ = connection;

        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        FlowTrace? completedTrace = null;
        recorder.TraceCompleted += trace => completedTrace = trace;

        var client = host.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/orders/1");
        request.Headers.Add("X-NetApiFlow-Demo-Delay-Ms", "250");
        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        completedTrace.ShouldNotBeNull();
        completedTrace!.DurationMicros!.Value.ShouldBeGreaterThanOrEqualTo(250_000);
    }
}
