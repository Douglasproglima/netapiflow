using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using NetApiFlow.EntityFrameworkCore;
using NetApiFlow.Marking;
using Shouldly;

namespace NetApiFlow.SqlClient.IntegrationTests;

public sealed class SqlServerConnectionString(string value)
{
    public string Value { get; } = value;
}

public sealed record CurrentActivity(string Label, string? Id, bool? IsStopped, string? Source);

public interface IRawSqlUserRepository
{
    Task<object?> CountAsync();
}

public sealed class RawSqlUserRepository(SqlServerConnectionString connectionString) : IRawSqlUserRepository
{
    public async Task<object?> CountAsync()
    {
        await using var connection = new SqlConnection(connectionString.Value);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT count(*) FROM Users", connection);
        return await command.ExecuteScalarAsync();
    }
}

[ApiController]
[Route("api/sql-in-step")]
public sealed class SqlInStepController(SqlServerConnectionString connectionString, IRawSqlUserRepository repository, IServiceProvider services) : ControllerBase
{
    [HttpGet("raw")]
    public async Task<IActionResult> Raw()
    {
        await using var connection = new SqlConnection(connectionString.Value);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT count(*) FROM Users", connection);
        return Ok(await command.ExecuteScalarAsync());
    }

    [HttpGet("repository")]
    public async Task<IActionResult> Repository() => Ok(await repository.CountAsync());

    [HttpGet("database-step")]
    public async Task<IActionResult> DatabaseStep()
    {
        using var step = Flow.Step("ManualDatabase", "Count", ComponentType.Database);
        await using var connection = new SqlConnection(connectionString.Value);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT count(*) FROM Users", connection);
        return Ok(await command.ExecuteScalarAsync());
    }

    [HttpGet("several-commands")]
    public async Task<IActionResult> SeveralCommands()
    {
        var seen = new List<CurrentActivity> { Snapshot("before") };
        await using var connection = new SqlConnection(connectionString.Value);
        await connection.OpenAsync();
        foreach (var (label, table) in new[] { ("first", "Users"), ("second", "Orders"), ("third", "Users") })
        {
            await using var command = new SqlCommand($"SELECT count(*) FROM dbo.{table}", connection);
            await command.ExecuteScalarAsync();
            seen.Add(Snapshot(label));
        }

        using (Flow.Step("Service", "Work", ComponentType.Service))
        {
        }

        return Ok(seen);
    }

    [HttpGet("then-driver-activity")]
    public async Task<IActionResult> ThenDriverActivity()
    {
        await using var connection = new SqlConnection(connectionString.Value);
        await connection.OpenAsync();
        await using (var command = new SqlCommand("SELECT count(*) FROM dbo.Users", connection))
        {
            await command.ExecuteScalarAsync();
        }

        using var npgsqlSource = new ActivitySource("Npgsql");
        using (var activity = npgsqlSource.StartActivity("postgresql"))
        {
            activity?.SetTag("db.query.text", "SELECT \"Id\" FROM \"Orders\"");
        }

        return Ok();
    }

    [HttpGet("ef-then-raw")]
    public async Task<IActionResult> EntityFrameworkThenRaw()
    {
        var seen = new List<CurrentActivity> { Snapshot("before") };
        await using (var context = services.GetRequiredService<SqlServerTestDbContext>())
        {
            await context.Users.CountAsync();
        }

        seen.Add(Snapshot("after ef"));
        await using var connection = new SqlConnection(connectionString.Value);
        await connection.OpenAsync();
        await using (var command = new SqlCommand("SELECT count(*) FROM dbo.Orders", connection))
        {
            await command.ExecuteScalarAsync();
        }

        seen.Add(Snapshot("after raw"));
        using (Flow.Step("Service", "Work", ComponentType.Service))
        {
        }

        return Ok(seen);
    }

    [HttpGet("failing-command")]
    public async Task<IActionResult> FailingCommand()
    {
        var seen = new List<CurrentActivity> { Snapshot("before") };
        await using var connection = new SqlConnection(connectionString.Value);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT * FROM dbo.TableThatDoesNotExist", connection);
        try
        {
            await command.ExecuteScalarAsync();
        }
        catch (SqlException)
        {
        }

        seen.Add(Snapshot("after failure"));
        using (Flow.Step("Service", "Work", ComponentType.Service))
        {
        }

        return Ok(seen);
    }

    [HttpGet("reader-command")]
    public async Task<IActionResult> ReaderCommand()
    {
        var seen = new List<CurrentActivity> { Snapshot("before") };
        await using var connection = new SqlConnection(connectionString.Value);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT Id FROM dbo.Users", connection);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            seen.Add(Snapshot("reader open"));
            while (await reader.ReadAsync())
            {
            }
        }

        seen.Add(Snapshot("reader closed"));
        using (Flow.Step("Service", "Work", ComponentType.Service))
        {
        }

        return Ok(seen);
    }

    [HttpGet("cancelled-command")]
    public async Task<IActionResult> CancelledCommand()
    {
        var seen = new List<CurrentActivity> { Snapshot("before") };
        await using var connection = new SqlConnection(connectionString.Value);
        await connection.OpenAsync();
        await using var command = new SqlCommand("WAITFOR DELAY '00:00:10'; SELECT 1", connection);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try
        {
            await command.ExecuteScalarAsync(cancellation.Token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or SqlException)
        {
        }

        seen.Add(Snapshot("after cancellation"));
        using (Flow.Step("Service", "Work", ComponentType.Service))
        {
        }

        return Ok(seen);
    }

    private static CurrentActivity Snapshot(string label) =>
        new(label, Activity.Current?.Id, Activity.Current?.IsStopped, Activity.Current?.Source.Name);

    [HttpGet("ef")]
    public async Task<IActionResult> EntityFramework()
    {
        await using var context = services.GetRequiredService<SqlServerTestDbContext>();
        return Ok(await context.Users.CountAsync());
    }
}

[Collection(SqlServerTestGroup.Name)]
public sealed class SqlClientCommandInsideStepIntegrationTests(SqlServerContainerFixture fixture)
{
    private async Task<(IHost Host, HttpClient Client)> StartHostAsync(
        bool withInterceptor = true,
        ExternalActivitySource externalSources = ExternalActivitySource.SqlClient)
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
                    services.AddSingleton(new SqlServerConnectionString(fixture.ConnectionString));
                    services.AddFlowTracked<IRawSqlUserRepository, RawSqlUserRepository>(ComponentType.Repository, ServiceLifetime.Singleton);
                    services.AddTransient(serviceProvider =>
                    {
                        var builder = new DbContextOptionsBuilder<SqlServerTestDbContext>().UseSqlServer(fixture.ConnectionString);
                        if (withInterceptor)
                        {
                            builder.AddInterceptors(serviceProvider.GetRequiredService<FlowDbCommandInterceptor>());
                        }

                        return new SqlServerTestDbContext(builder.Options);
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapControllers();
                        endpoints.MapGet("/mini/step", async (SqlServerConnectionString connectionString) =>
                        {
                            using var step = Flow.Step("MiniEndpoint", "Count");
                            await using var connection = new SqlConnection(connectionString.Value);
                            await connection.OpenAsync();
                            await using var command = new SqlCommand("SELECT count(*) FROM Users", connection);
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

    private static async Task<(IReadOnlyList<FlowSpan> Spans, IReadOnlyList<CurrentActivity> Current)> GetWithCurrentActivityAsync(IHost host, HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var current = JsonSerializer.Deserialize<List<CurrentActivity>>(await response.Content.ReadAsStringAsync(), JsonSerializerOptions.Web)!;
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        return (Flow.GetRecentTrace(host.Services, traceId)!.Spans, current);
    }

    private static FlowSpan SingleDriverSpan(IReadOnlyList<FlowSpan> spans) =>
        spans.Single(span => span.Component == "SqlClient");

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
        spans.ShouldNotContain(span => span.Component == "SqlClient");
    }

    [Fact]
    public async Task EntityFrameworkQueryWithTheInterceptor_IsRecordedExactlyOnce()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var spans = await GetAsync(host, client, "/api/sql-in-step/ef");

        spans.Single(span => span.Type == ComponentType.Database).Component.ShouldBe("EntityFrameworkCore");
        spans.ShouldNotContain(span => span.Component == "SqlClient");
    }

    [Fact]
    public async Task EntityFrameworkQueryWithoutTheInterceptor_IsRecordedByTheDriver()
    {
        var (host, client) = await StartHostAsync(withInterceptor: false);
        using var _ = host;
        using var __ = client;

        var spans = await GetAsync(host, client, "/api/sql-in-step/ef");

        spans.Single(span => span.Type == ComponentType.Database).Component.ShouldBe("SqlClient");
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

        spans.ShouldNotContain(span => span.Component == "SqlClient");
    }

    [Fact]
    public async Task SeveralCommandsInOneMethod_RecordOneStepEachUnderTheCallerInOrder()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var (spans, _) = await GetWithCurrentActivityAsync(host, client, "/api/sql-in-step/several-commands");

        var controller = spans.Single(span => span.Type == ComponentType.Controller);
        var commands = spans.Where(span => span.Component == "SqlClient").OrderBy(span => span.Sequence).ToList();
        commands.Select(command => command.Operation).ShouldBe(["SELECT Users", "SELECT Orders", "SELECT Users"]);
        commands.ShouldAllBe(command => command.ParentSpanId == controller.SpanId && command.Status == FlowStatus.Success);
    }

    [Fact]
    public async Task AfterACommand_TheActivityCurrentToTheCallerIsStillTheCallersOwn()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var (_, current) = await GetWithCurrentActivityAsync(host, client, "/api/sql-in-step/several-commands");

        var before = current.Single(entry => entry.Label == "before");
        current.Where(entry => entry.Label != "before").ShouldAllBe(entry => entry.Id == before.Id && entry.IsStopped == false && entry.Source == before.Source);
    }

    [Fact]
    public async Task AStepOpenedAfterTheCommands_IsAChildOfTheCallerAndNotOfAFinishedSqlStep()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var (spans, _) = await GetWithCurrentActivityAsync(host, client, "/api/sql-in-step/several-commands");

        var controller = spans.Single(span => span.Type == ComponentType.Controller);
        spans.Single(span => span.Component == "Service").ParentSpanId.ShouldBe(controller.SpanId);
    }

    [Fact]
    public async Task ADriverActivityAfterASqlClientCommand_IsStillRecorded()
    {
        var (host, client) = await StartHostAsync(externalSources: ExternalActivitySource.SqlClient | ExternalActivitySource.Npgsql);
        using var _ = host;
        using var __ = client;

        var spans = await GetAsync(host, client, "/api/sql-in-step/then-driver-activity");

        spans.Select(span => span.Component).Where(component => component is "SqlClient" or "Npgsql").ShouldBe(["SqlClient", "Npgsql"]);
    }

    [Fact]
    public async Task AnEfCoreQueryThenARawCommandInOneMethod_RecordTwoStepsAndLeaveTheCallerActivityCurrent()
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var (spans, current) = await GetWithCurrentActivityAsync(host, client, "/api/sql-in-step/ef-then-raw");

        var controller = spans.Single(span => span.Type == ComponentType.Controller);
        spans.Where(span => span.Type == ComponentType.Database).Select(span => span.Component).ShouldBe(["EntityFrameworkCore", "SqlClient"]);
        spans.Where(span => span.Type == ComponentType.Database).ShouldAllBe(span => span.ParentSpanId == controller.SpanId);
        spans.Single(span => span.Component == "Service").ParentSpanId.ShouldBe(controller.SpanId);
        var before = current.Single(entry => entry.Label == "before");
        current.Where(entry => entry.Label != "before").ShouldAllBe(entry => entry.Id == before.Id);
    }

    [Theory]
    [InlineData("failing-command")]
    [InlineData("reader-command")]
    [InlineData("cancelled-command")]
    public async Task WhateverTheOutcomeOfTheCommand_TheCallerActivityStaysCurrentAndTheNextStepIsItsChild(string route)
    {
        var (host, client) = await StartHostAsync();
        using var _ = host;
        using var __ = client;

        var (spans, current) = await GetWithCurrentActivityAsync(host, client, $"/api/sql-in-step/{route}");

        var controller = spans.Single(span => span.Type == ComponentType.Controller);
        var before = current.Single(entry => entry.Label == "before");
        current.Where(entry => entry.Label != "before").ShouldAllBe(entry => entry.Id == before.Id && entry.IsStopped == false);
        spans.Single(span => span.Component == "Service").ParentSpanId.ShouldBe(controller.SpanId);
        spans.Single(span => span.Component == "SqlClient").ParentSpanId.ShouldBe(controller.SpanId);
    }
}
