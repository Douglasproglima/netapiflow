using System.Collections.Concurrent;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetApiFlow.Domain;
using NetApiFlow.Marking;
using NetApiFlow.MediatR;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;
using SampleData = SampleApi.Controllers.Data;

namespace NetApiFlow.IntegrationTests;

public interface IPlacementHandler
{
    string Handle();
}

public sealed class PlacementHandler(IPlacementService service) : IPlacementHandler
{
    public string Handle() => service.Place();
}

public interface IPlacementService
{
    string Place();
}

public sealed class PlacementService(IPlacementRepository repository) : IPlacementService
{
    public string Place() => repository.Find();
}

public interface IPlacementRepository
{
    string Find();
}

public sealed class PlacementRepository : IPlacementRepository
{
    public string Find() => "stored";
}

public sealed record PlaceOrder : IRequest<string>;

public sealed class PlaceOrderHandler : IRequestHandler<PlaceOrder, string>
{
    public Task<string> Handle(PlaceOrder request, CancellationToken cancellationToken) => Task.FromResult("placed");
}

public sealed class FlowConventionIntegrationTests
{
    private static readonly System.Reflection.Assembly TestAssembly = typeof(PlacementService).Assembly;

    private static void RegisterTheApp(IServiceCollection services)
    {
        services.AddScoped<IPlacementHandler, PlacementHandler>();
        services.AddScoped<IPlacementService, PlacementService>();
        services.AddScoped<IPlacementRepository, PlacementRepository>();
    }

    [Fact]
    public async Task TrackFlowByConvention_ForTheAppsServicesRepositoriesAndHandlers_RecordsEachWithItsComponentType()
    {
        await using var app = await ConventionHost.StartAsync(services =>
        {
            RegisterTheApp(services);
            FlowTrackedServiceCollectionExtensions.TrackFlowByConventionOf(services, null, TestAssembly);
        });

        var trace = await app.TraceOfAsync("/place");

        trace.Spans.Single(span => span.Type == ComponentType.UseCase).Component.ShouldBe(nameof(PlacementHandler));
        trace.Spans.Single(span => span.Type == ComponentType.Service).Component.ShouldBe(nameof(PlacementService));
        trace.Spans.Single(span => span.Type == ComponentType.Repository).Component.ShouldBe(nameof(PlacementRepository));
    }

    [Fact]
    public async Task TrackFlowByConvention_LeavesEveryDescriptorOfATypeThatIsNotTheAppsUntouched()
    {
        var services = new ServiceCollection();
        services.AddNetApiFlow();
        services.AddAuthorization();
        services.AddLogging();
        RegisterTheApp(services);
        var notTheApps = services.Where(descriptor => descriptor.ImplementationType?.Assembly != TestAssembly).ToList();

        FlowTrackedServiceCollectionExtensions.TrackFlowByConventionOf(services, null, TestAssembly);

        await Task.CompletedTask;
        notTheApps.ShouldNotBeEmpty();
        foreach (var descriptor in notTheApps)
        {
            services.ShouldContain(descriptor);
        }

        services.Single(descriptor => descriptor.ServiceType == typeof(IPlacementService)).ImplementationFactory.ShouldNotBeNull();
    }

    [Fact]
    public async Task TrackFlowByConvention_CalledBeforeTheRegistrations_LogsAWarningNamingTheTypesItMissed()
    {
        var logs = new CapturingLogs();
        await using var app = await ConventionHost.StartAsync(
            services =>
            {
                FlowTrackedServiceCollectionExtensions.TrackFlowByConventionOf(services, null, TestAssembly);
                RegisterTheApp(services);
            },
            logs);

        logs.Lines.ShouldContain(line =>
            line.Level == LogLevel.Warning
            && line.Message.Contains(typeof(PlacementService).FullName!, StringComparison.Ordinal)
            && line.Message.Contains("after your own registrations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TrackFlowByConvention_ForARegistrationMadeByFactory_LogsAWarningNamingTheService()
    {
        var logs = new CapturingLogs();
        await using var app = await ConventionHost.StartAsync(
            services =>
            {
                services.AddScoped<IPlacementRepository, PlacementRepository>();
                services.AddScoped<IPlacementService>(provider => new PlacementService(provider.GetRequiredService<IPlacementRepository>()));
                FlowTrackedServiceCollectionExtensions.TrackFlowByConventionOf(services, null, TestAssembly);
            },
            logs);

        logs.Lines.ShouldContain(line =>
            line.Level == LogLevel.Warning
            && line.Message.Contains(typeof(IPlacementService).FullName!, StringComparison.Ordinal)
            && line.Message.Contains("AddFlowTracked", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddNetApiFlowAlone_DecoratesNothing()
    {
        await using var app = await ConventionHost.StartAsync(RegisterTheApp);

        await using var scope = app.Host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPlacementService>().ShouldBeOfType<PlacementService>();
        scope.ServiceProvider.GetRequiredService<IPlacementRepository>().ShouldBeOfType<PlacementRepository>();
    }

    [Fact]
    public async Task DecorateFlowTracked_WithAPredicateThatMatchesNetApiFlowHostedServices_NeverProxiesThem()
    {
        await using var app = await ConventionHost.StartAsync(services =>
            services.DecorateFlowTracked(type => type.Name.EndsWith("Service", StringComparison.Ordinal)));

        var hostedServices = app.Host.Services.GetServices<IHostedService>().ToList();

        hostedServices.ShouldNotBeEmpty();
        hostedServices.ShouldNotContain(service => service is FlowInterceptorProxy);
    }

    [Fact]
    public async Task TrackFlowByConvention_ForAMediatRHandler_RecordsOneUseCaseStepAndNeverTwo()
    {
        await using var app = await ConventionHost.StartAsync(
            services =>
            {
                services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<PlaceOrder>());
                services.AddNetApiFlowMediatR();
                FlowTrackedServiceCollectionExtensions.TrackFlowByConventionOf(services, null, TestAssembly);
            },
            map: endpoints => endpoints.MapGet("/place", async (ISender sender) => await sender.Send(new PlaceOrder())));

        var trace = await app.TraceOfAsync("/place");

        trace.Spans.Count(span => span.Type == ComponentType.UseCase).ShouldBe(1);
    }

    [Fact]
    public void TrackFlowByConvention_ForARepositoryOfAnotherAssembly_DecoratesItOnlyWhenTheAssemblyIsIncluded()
    {
        var otherAssembly = typeof(SampleData.OrderRepository).Assembly;
        var withoutScope = new ServiceCollection();
        withoutScope.AddScoped<SampleData.IOrderRepository, SampleData.OrderRepository>();
        var withScope = new ServiceCollection();
        withScope.AddScoped<SampleData.IOrderRepository, SampleData.OrderRepository>();

        FlowTrackedServiceCollectionExtensions.TrackFlowByConventionOf(withoutScope, null, TestAssembly);
        FlowTrackedServiceCollectionExtensions.TrackFlowByConventionOf(withScope, options => options.IncludeAssembly(otherAssembly), TestAssembly);

        withoutScope.Single(descriptor => descriptor.ServiceType == typeof(SampleData.IOrderRepository)).ImplementationType.ShouldBe(typeof(SampleData.OrderRepository));
        withScope.Single(descriptor => descriptor.ServiceType == typeof(SampleData.IOrderRepository)).ImplementationType.ShouldBeNull();
    }

    private sealed class CapturingLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _lines = new();

        public IReadOnlyCollection<(LogLevel Level, string Message)> Lines => _lines.ToArray();

        public ILogger CreateLogger(string categoryName) => new Logger(_lines);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<(LogLevel Level, string Message)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue((logLevel, formatter(state, exception)));
        }
    }

    private sealed class ConventionHost : IAsyncDisposable
    {
        private readonly ConcurrentQueue<FlowTrace> _completed = new();

        private ConventionHost(IHost host)
        {
            Host = host;
            host.Services.GetRequiredService<FlowRecorder>().TraceCompleted += _completed.Enqueue;
        }

        public IHost Host { get; }

        public static async Task<ConventionHost> StartAsync(
            Action<IServiceCollection> register,
            CapturingLogs? logs = null,
            Action<IEndpointRouteBuilder>? map = null)
        {
            var host = await Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    if (logs is not null)
                    {
                        logging.AddProvider(logs);
                    }
                })
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddNetApiFlow();
                        register(services);
                    });
                    web.Configure(pipeline =>
                    {
                        pipeline.UseRouting();
                        pipeline.UseEndpoints(endpoints =>
                        {
                            if (map is not null)
                            {
                                map(endpoints);
                            }
                            else
                            {
                                endpoints.MapGet("/place", (IPlacementHandler handler) => handler.Handle());
                            }
                        });
                    });
                })
                .StartAsync();
            return new ConventionHost(host);
        }

        public async Task<FlowTraceDetail> TraceOfAsync(string path)
        {
            var before = _completed.Count;
            using var response = await Host.GetTestServer().CreateClient().GetAsync(path);
            response.EnsureSuccessStatusCode();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (_completed.Count <= before && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            var trace = _completed.ElementAt(before);
            return Flow.GetRecentTrace(Host.Services, trace.TraceId)!;
        }

        public async ValueTask DisposeAsync()
        {
            await Host.StopAsync();
            Host.Dispose();
        }
    }
}
