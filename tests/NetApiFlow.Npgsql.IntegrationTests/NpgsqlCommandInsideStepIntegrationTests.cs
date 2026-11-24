using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using NetApiFlow.EntityFrameworkCore;
using NetApiFlow.Marking;
using Npgsql;
using Shouldly;

namespace NetApiFlow.Npgsql.IntegrationTests;

public interface IRawUserRepository
{
    Task<object?> CountAsync();
}

public sealed class RawUserRepository(NpgsqlDataSource dataSource) : IRawUserRepository
{
    public async Task<object?> CountAsync()
    {
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM \"Users\"");
        return await command.ExecuteScalarAsync();
    }
}

[ApiController]
[Route("api/sql-in-step")]
public sealed class SqlInStepController(NpgsqlDataSource dataSource, IRawUserRepository repository, IServiceProvider services) : ControllerBase
{
    [HttpGet("raw")]
    public async Task<IActionResult> Raw()
    {
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM \"Users\"");
        return Ok(await command.ExecuteScalarAsync());
    }

    [HttpGet("repository")]
    public async Task<IActionResult> Repository() => Ok(await repository.CountAsync());

    [HttpGet("database-step")]
    public async Task<IActionResult> DatabaseStep()
    {
        using var step = Flow.Step("ManualDatabase", "Count", ComponentType.Database);
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM \"Users\"");
        return Ok(await command.ExecuteScalarAsync());
    }

    [HttpGet("ef")]
    public async Task<IActionResult> EntityFramework()
    {
        await using var context = services.GetRequiredService<NpgsqlTestDbContext>();
        return Ok(await context.Users.CountAsync());
    }
}

[Collection(NpgsqlTestGroup.Name)]
public sealed class NpgsqlCommandInsideStepIntegrationTests(NpgsqlContainerFixture fixture)
{
    private async Task<(IHost Host, HttpClient Client)> StartHostAsync(
        bool withInterceptor = true,
        ExternalActivitySource externalSources = ExternalActivitySource.Npgsql)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseKestrel();
                webBuilder.UseUrls("http://127.0.0.1:0");
                webBuilder.UseEnvironment(Environments.Production);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddControllers().AddApplicationPart(typeof(SqlInStepController).Assembly);
                    services.AddNetApiFlow(options =>
                    {
                        options.IncludePaths("/api/**", "/mini/**");
                        options.EmitTraceIdHeader = true;
                        options.ExternalSources = externalSources;
                    });
                    services.AddNetApiFlowEntityFrameworkCore();
                    services.AddSingleton(fixture.DataSource);
                    services.AddFlowTracked<IRawUserRepository, RawUserRepository>(ComponentType.Repository, ServiceLifetime.Singleton);
                    services.AddTransient(serviceProvider => withInterceptor
                        ? NpgsqlTestDbContext.Create(fixture.DataSource, serviceProvider.GetRequiredService<FlowDbCommandInterceptor>())
                        : NpgsqlTestDbContext.Create(fixture.DataSource));
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapControllers();
                        endpoints.MapGet("/mini/step", async (NpgsqlDataSource dataSource) =>
                        {
                            using var step = Flow.Step("MiniEndpoint", "Count");
                            await using var command = dataSource.CreateCommand("SELECT count(*) FROM \"Users\"");
                            return await command.ExecuteScalarAsync();
                        });
                    });
                });
            })
            .Build();

        await host.StartAsync();
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (host, new HttpClient { BaseAddress = new Uri(address) });
    }

    private static async Task<IReadOnlyList<FlowSpan>> GetAsync(IHost host, HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        return Flow.GetRecentTrace(host.Services, traceId)!.Spans;
    }

    private static FlowSpan SingleDriverSpan(IReadOnlyList<FlowSpan> spans) =>
        spans.Single(span => span.Component == "Npgsql");

    [Fact]
    public async Task RawCommandInsideAController_IsRecordedAsAChildOfTheControllerSpan()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var spans = await GetAsync(host, client, "/api/sql-in-step/raw");

        var controllerSpan = spans.Single(span => span.Type == ComponentType.Controller);
        SingleDriverSpan(spans).ParentSpanId.ShouldBe(controllerSpan.SpanId);
    }

    [Fact]
    public async Task RawCommandInsideAManualStepOnAMinimalEndpoint_IsRecordedAsAChildOfTheStep()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var spans = await GetAsync(host, client, "/mini/step");

        var step = spans.Single(span => span.Component == "MiniEndpoint");
        SingleDriverSpan(spans).ParentSpanId.ShouldBe(step.SpanId);
    }

    [Fact]
    public async Task RawCommandInsideATrackedRepository_IsRecordedAsAChildOfTheRepositorySpan()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var spans = await GetAsync(host, client, "/api/sql-in-step/repository");

        var repositorySpan = spans.Single(span => span.Type == ComponentType.Repository);
        SingleDriverSpan(spans).ParentSpanId.ShouldBe(repositorySpan.SpanId);
    }

    [Fact]
    public async Task RawCommandInsideAManualDatabaseStep_IsRecordedOnlyByThatStep()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var spans = await GetAsync(host, client, "/api/sql-in-step/database-step");

        spans.Single(span => span.Type == ComponentType.Database).Component.ShouldBe("ManualDatabase");
        spans.ShouldNotContain(span => span.Component == "Npgsql");
    }

    [Fact]
    public async Task EntityFrameworkQueryWithTheInterceptor_IsRecordedExactlyOnce()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var spans = await GetAsync(host, client, "/api/sql-in-step/ef");

        spans.Single(span => span.Type == ComponentType.Database).Component.ShouldBe("EntityFrameworkCore");
        spans.ShouldNotContain(span => span.Component == "Npgsql");
    }

    [Fact]
    public async Task EntityFrameworkQueryWithoutTheInterceptor_IsRecordedByTheDriver()
    {
        var (host, client) = await StartHostAsync(withInterceptor: false);
        using var _ = host;
        using var __ = client;

        var spans = await GetAsync(host, client, "/api/sql-in-step/ef");

        spans.Single(span => span.Type == ComponentType.Database).Component.ShouldBe("Npgsql");
    }

    [Fact]
    public async Task WithAnotherListenerSamplingTheNpgsqlSource_TheDriverSpanAndTheInterceptorSpanAreStillRecordedOnce()
    {
        using var otherListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(otherListener);

        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var rawSpans = await GetAsync(host, client, "/api/sql-in-step/raw");
        var efSpans = await GetAsync(host, client, "/api/sql-in-step/ef");

        rawSpans.Count(span => span.Component == "Npgsql").ShouldBe(1);
        efSpans.Single(span => span.Type == ComponentType.Database).Component.ShouldBe("EntityFrameworkCore");
    }

    [Fact]
    public async Task ConcurrentRequestsInsideAController_EachRecordTheirOwnDriverSpan()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => GetAsync(host, client, "/api/sql-in-step/raw")));

        foreach (var spans in results)
        {
            var controllerSpan = spans.Single(span => span.Type == ComponentType.Controller);
            SingleDriverSpan(spans).ParentSpanId.ShouldBe(controllerSpan.SpanId);
        }

        results.SelectMany(spans => spans.Select(span => span.TraceId)).Distinct().Count().ShouldBe(16);
    }

    [Fact]
    public async Task RawCommandInsideAController_WithTheSourceExplicitlyOff_IsNotRecorded()
    {
        var (host, client) = await StartHostAsync(externalSources: ExternalActivitySource.None);
        using var _ = host;
        using var __ = client;

        var spans = await GetAsync(host, client, "/api/sql-in-step/raw");

        spans.ShouldNotContain(span => span.Component == "Npgsql");
    }
}
