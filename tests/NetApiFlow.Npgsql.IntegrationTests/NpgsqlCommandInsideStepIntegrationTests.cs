using System.Diagnostics;
using System.Text.Json;
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

public sealed record NpgsqlServer(string ConnectionString);

public sealed record NpgsqlCurrentActivity(string Label, string? Id, bool? IsStopped);

[ApiController]
[Route("api/sql-in-step")]
public sealed class SqlInStepController(NpgsqlDataSource dataSource, IRawUserRepository repository, IServiceProvider services, NpgsqlServer server) : ControllerBase
{
    public const string RenamedCommandSpan = "CONNECT custom";

    [HttpGet("several-commands")]
    public async Task<IActionResult> SeveralCommands()
    {
        var seen = new List<NpgsqlCurrentActivity> { Snapshot("before") };
        foreach (var label in new[] { "first", "second", "third" })
        {
            await using var command = dataSource.CreateCommand("SELECT count(*) FROM \"Users\"");
            await command.ExecuteScalarAsync();
            seen.Add(Snapshot(label));
        }

        using (Flow.Step("Service", "Work", ComponentType.Service))
        {
        }

        return Ok(seen);
    }

    private static NpgsqlCurrentActivity Snapshot(string label) =>
        new(label, Activity.Current?.Id, Activity.Current?.IsStopped);

    [HttpGet("fresh-connections")]
    public async Task<IActionResult> FreshConnections()
    {
        await using var unpooled = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(server.ConnectionString) { Pooling = false }.ConnectionString);
        await using (var count = unpooled.CreateCommand("SELECT count(*) FROM \"Users\""))
        {
            await count.ExecuteScalarAsync();
        }

        await using var first = unpooled.CreateCommand("SELECT \"Id\" FROM \"Users\" ORDER BY \"Id\" LIMIT 1");
        return Ok(await first.ExecuteScalarAsync());
    }

    [HttpGet("wrong-password")]
    public async Task<IActionResult> WrongPassword()
    {
        var wrongPassword = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Pooling = false, Password = "not-the-password" };
        await using var connection = new NpgsqlConnection(wrongPassword.ConnectionString);
        await connection.OpenAsync();
        return Ok();
    }

    [HttpGet("renamed-between-normal-commands")]
    public async Task<IActionResult> RenamedBetweenNormalCommands()
    {
        await using var renamed = RenamedDataSource();
        await using (var before = dataSource.CreateCommand("SELECT count(*) FROM \"Users\""))
        {
            await before.ExecuteScalarAsync();
        }

        await using (var middle = renamed.CreateCommand("SELECT \"Id\" FROM \"Users\" ORDER BY \"Id\" LIMIT 1"))
        {
            await middle.ExecuteScalarAsync();
        }

        await using var after = dataSource.CreateCommand("SELECT max(\"Id\") FROM \"Users\"");
        return Ok(await after.ExecuteScalarAsync());
    }

    [HttpGet("renamed-failing-command")]
    public async Task<IActionResult> RenamedFailingCommand()
    {
        await using var renamed = RenamedDataSource();
        await using var command = renamed.CreateCommand("SELECT \"Id\" FROM \"TableThatDoesNotExist\"");
        return Ok(await command.ExecuteScalarAsync());
    }

    private NpgsqlDataSource RenamedDataSource()
    {
        var builder = new NpgsqlDataSourceBuilder(server.ConnectionString);
        builder.ConfigureTracing(tracing => tracing.ConfigureCommandSpanNameProvider(_ => RenamedCommandSpan));
        return builder.Build();
    }

    [HttpGet("renamed-command")]
    public async Task<IActionResult> RenamedCommand()
    {
        var builder = new NpgsqlDataSourceBuilder(server.ConnectionString);
        builder.ConfigureTracing(tracing => tracing.ConfigureCommandSpanNameProvider(_ => RenamedCommandSpan));
        await using var renamed = builder.Build();
        await using var command = renamed.CreateCommand("SELECT count(*) FROM \"Users\"");
        return Ok(await command.ExecuteScalarAsync());
    }

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
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

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
                    services.AddSingleton(new NpgsqlServer(fixture.ConnectionString));
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

    private static async Task<FlowTraceDetail> GetFailedTraceAsync(IHost host, HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.IsSuccessStatusCode.ShouldBeFalse();
        var store = host.Services.GetRequiredService<ITraceStore>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var failed = await store.QueryAsync(new TraceQuery { Status = FlowStatus.Error }, timeout.Token);
            if (failed.Items.FirstOrDefault(summary => summary.RouteTemplate.EndsWith(path.Split('/').Last(), StringComparison.Ordinal)) is { } summary)
            {
                return (await store.GetAsync(summary.TraceId, timeout.Token))!;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }
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

    [Fact]
    public async Task CommandsOnFreshPhysicalConnections_RecordOnlyTheCommandsAndNoQueryShapeReachesTheNPlusOneThreshold()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var spans = await GetAsync(host, client, "/api/sql-in-step/fresh-connections");

        var driverSpans = spans.Where(span => span.Component == "Npgsql").ToList();
        driverSpans.Select(span => span.Operation).ShouldBe(["SELECT Users", "SELECT Users"]);
        spans.Where(span => span.Type == ComponentType.Database)
            .GroupBy(span => (span.Component, span.Operation))
            .ShouldAllBe(group => group.Count() < NPlusOneThreshold);
    }

    [Fact]
    public async Task ConnectionRefusedForAWrongPassword_FailsTheStepThatOpenedItAndRecordsNoConnectionStep()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var trace = await GetFailedTraceAsync(host, client, "/api/sql-in-step/wrong-password");

        trace.Trace.Status.ShouldBe(FlowStatus.Error);
        trace.Spans.Single(span => span.Type == ComponentType.Controller).Status.ShouldBe(FlowStatus.Error);
        trace.Spans.ShouldNotContain(span => span.Component == "Npgsql");
    }

    [Fact]
    public async Task CommandTheAppNamesLikeAConnection_IsStillRecordedWithItsSql()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var spans = await GetAsync(host, client, "/api/sql-in-step/renamed-command");

        var span = SingleDriverSpan(spans);
        span.Operation.ShouldBe("SELECT Users");
        span.Status.ShouldBe(FlowStatus.Success);
        span.ParentSpanId.ShouldBe(spans.Single(candidate => candidate.Type == ComponentType.Controller).SpanId);
    }

    [Fact]
    public async Task CommandTheAppNamesLikeAConnection_BetweenTwoNormalCommands_IsRecordedLikeThem()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var spans = await GetAsync(host, client, "/api/sql-in-step/renamed-between-normal-commands");

        var controller = spans.Single(span => span.Type == ComponentType.Controller);
        var commands = spans.Where(span => span.Component == "Npgsql").OrderBy(span => span.Sequence).ToList();
        commands.Count.ShouldBe(3);
        commands.ShouldAllBe(command => command.ParentSpanId == controller.SpanId
            && command.Operation == "SELECT Users"
            && command.Status == FlowStatus.Success
            && command.DurationMicros > 0
            && command.EndedAt > command.StartedAt);
        commands.Select(command => command.StartedAt).ShouldBeInOrder();
        commands[1].StartedAt.ShouldBeGreaterThanOrEqualTo(commands[0].EndedAt!.Value);
        commands[2].StartedAt.ShouldBeGreaterThanOrEqualTo(commands[1].EndedAt!.Value);
    }

    [Fact]
    public async Task CommandTheAppNamesLikeAConnection_WhenItFails_IsTheErrorOriginLikeANormalCommand()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var trace = await GetFailedTraceAsync(host, client, "/api/sql-in-step/renamed-failing-command");

        var command = trace.Spans.Single(span => span.Component == "Npgsql");
        command.Operation.ShouldBe("SELECT TableThatDoesNotExist");
        command.Status.ShouldBe(FlowStatus.Error);
        command.IsErrorOrigin.ShouldBeTrue();
        command.ErrorType.ShouldBe("Npgsql.PostgresException");
        command.ParentSpanId.ShouldBe(trace.Spans.Single(span => span.Type == ComponentType.Controller).SpanId);
    }

    private const int NPlusOneThreshold = 3;

    [Fact]
    public async Task SeveralCommandsInOneMethod_RecordOneStepEachAndLeaveTheCallerActivityCurrent()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var response = await client.GetAsync("/api/sql-in-step/several-commands");
        response.EnsureSuccessStatusCode();
        var current = JsonSerializer.Deserialize<List<NpgsqlCurrentActivity>>(await response.Content.ReadAsStringAsync(), WebOptions)!;
        var spans = Flow.GetRecentTrace(host.Services, response.Headers.GetValues("X-NetApiFlow-TraceId").Single())!.Spans;

        var controller = spans.Single(span => span.Type == ComponentType.Controller);
        var commands = spans.Where(span => span.Component == "Npgsql").OrderBy(span => span.Sequence).ToList();
        commands.Count.ShouldBe(3);
        commands.ShouldAllBe(command => command.ParentSpanId == controller.SpanId);
        spans.Single(span => span.Component == "Service").ParentSpanId.ShouldBe(controller.SpanId);
        var before = current.Single(entry => entry.Label == "before");
        current.Where(entry => entry.Label != "before").ShouldAllBe(entry => entry.Id == before.Id && entry.IsStopped == false);
    }
}
