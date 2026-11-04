using System.Collections.Concurrent;
using System.Net;
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
using NetApiFlow.Recording;
using Shouldly;
using Xunit;
using Sample = SampleApi.Controllers;

namespace NetApiFlow.IntegrationTests;

/// <summary>
/// Verifies the Controllers sample (tarefa 2.8) produces the full
/// Controller → Service → Repository → Database flow automatically, with no manual
/// <c>NetApiFlow.Step(...)</c> call anywhere in the sample's business code.
///
/// The sample's own <c>Program.cs</c> targets PostgreSQL via Npgsql, matching the product spec.
/// This test builds a separate host reusing the sample's Controller/Service/Repository/DbContext
/// classes directly with SQLite in-memory, so the flow can be exercised without a real PostgreSQL
/// server available in this environment; the swap only happens here, never in the shipped sample.
/// End-to-end verification against a real PostgreSQL instance is still pending and should be done
/// wherever Docker/PostgreSQL is available (see resumo de fim de fase).
/// </summary>
public class SampleApiControllersTests
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
                    services.AddControllers().AddApplicationPart(typeof(Sample.Controllers.OrdersController).Assembly);
                    services.AddHttpClient();

                    services.AddNetApiFlow(o => o.IncludePaths("/api/**"));
                    services.AddNetApiFlowEntityFrameworkCore();

                    // Registered directly here (not via the sample's Program.cs, which wires Npgsql)
                    // so this test never pulls Npgsql's provider services into the same container.
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
                new Sample.Data.Order { Id = 2, CustomerName = "Alan Turing", Amount = 75.00m, Status = "Shipped" },
                new Sample.Data.Order { Id = 3, CustomerName = "Grace Hopper", Amount = 42.00m, Status = "Pending" });
            dbContext.SaveChanges();
        }

        return (host, connection);
    }

    [Fact]
    public async Task Get_SuccessScenario_ProducesControllerServiceRepositoryDatabaseSpans()
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

        spans.ShouldContain(s => s.Type == ComponentType.Controller && s.Component == "OrdersController");
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

        var serviceSpan = spans.Single(s => s.Type == ComponentType.Service);
        serviceSpan.Status.ShouldBe(FlowStatus.Error);
        serviceSpan.IsErrorOrigin.ShouldBeTrue();

        var controllerSpan = spans.Single(s => s.Type == ComponentType.Controller);
        controllerSpan.Status.ShouldBe(FlowStatus.Error);
        controllerSpan.IsErrorOrigin.ShouldBeFalse();
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
        var serviceSpan = spansByTrace[traceId].Single(s => s.Type == ComponentType.Service);

        serviceSpan.DurationMicros!.Value.ShouldBeGreaterThanOrEqualTo(250_000);
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
}
