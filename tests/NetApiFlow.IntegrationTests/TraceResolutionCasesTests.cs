using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using NetApiFlow.EntityFrameworkCore;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public sealed class TraceResolutionCasesTests : IAsyncLifetime, IDisposable
{
    private const string CaseComponent = "Case";
    private static readonly ActivitySource ExplicitParentSource = new("NetApiFlow");

    private readonly SqliteConnection _sqlite = new("DataSource=:memory:");
    private readonly HttpListener _upstream = new();
    private readonly ManualResetEventSlim _lateStepFinished = new();
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var upstreamUrl = StartUpstream();
        _sqlite.Open();
        _host = await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow(options =>
                    {
                        options.EmitTraceIdHeader = true;
                        options.ExternalSources = ExternalActivitySource.HttpClient;
                    });
                    services.AddNetApiFlowEntityFrameworkCore();
                    services.AddDbContext<CasesDbContext>((provider, options) =>
                        options.UseSqlite(_sqlite).AddInterceptors(provider.GetRequiredService<FlowDbCommandInterceptor>()));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => MapCases(endpoints, upstreamUrl));
                });
            })
            .StartAsync();
        using (var scope = _host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<CasesDbContext>().Database.EnsureCreatedAsync();
        }

        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    public void Dispose()
    {
        _upstream.Close();
        _sqlite.Dispose();
        _lateStepFinished.Dispose();
    }

    [Fact]
    public async Task Step_InsideTaskRun_IsRecordedInTheTraceOfTheRequest()
    {
        var spans = await SpansOfTheRequestAsync("/cases/task-run");

        spans.ShouldContain(span => span.Component == CaseComponent && span.Operation == "task-run");
    }

    [Fact]
    public async Task Steps_InsideTaskWhenAll_AreAllRecordedInTheTraceOfTheRequest()
    {
        var spans = await SpansOfTheRequestAsync("/cases/when-all");

        spans.Count(span => span.Component == CaseComponent && span.Operation.StartsWith("when-all", StringComparison.Ordinal)).ShouldBe(3);
    }

    [Fact]
    public async Task Steps_InsideAnAsyncIterator_AreRecordedInTheTraceOfTheRequest()
    {
        var spans = await SpansOfTheRequestAsync("/cases/async-enumerable");

        spans.Count(span => span.Component == CaseComponent && span.Operation.StartsWith("async-enumerable", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Fact]
    public async Task OutboundHttpClientCall_IsRecordedAsAnHttpStepInTheTraceOfTheRequest()
    {
        var spans = await SpansOfTheRequestAsync("/cases/http-client");

        spans.ShouldContain(span => span.Type == ComponentType.Http);
    }

    [Fact]
    public async Task EntityFrameworkCommand_IsRecordedAsADatabaseStepInTheTraceOfTheRequest()
    {
        var spans = await SpansOfTheRequestAsync("/cases/efcore");

        spans.ShouldContain(span => span.Type == ComponentType.Database);
    }

    [Fact]
    public async Task Step_StartedWhileExecutionContextFlowIsSuppressed_IsNotRecorded()
    {
        var spans = await SpansOfTheRequestAsync("/cases/suppress-flow");

        spans.ShouldNotContain(span => span.Component == CaseComponent);
    }

    [Fact]
    public async Task Activity_StartedWithTheRequestContextAsExplicitParent_IsRecordedInTheTraceOfTheRequest()
    {
        var spans = await SpansOfTheRequestAsync("/cases/explicit-parent");

        spans.ShouldContain(span => span.Operation == "explicit-parent");
    }

    [Fact]
    public async Task Activity_StartedWithTheRequestContextAsExplicitParentWithoutExecutionContextFlow_IsNotRecorded()
    {
        var spans = await SpansOfTheRequestAsync("/cases/explicit-parent-without-flow");

        spans.ShouldNotContain(span => span.Operation == "explicit-parent-without-flow");
    }

    [Fact]
    public async Task Step_WithActivityCurrentRestoredByHandWithoutExecutionContextFlow_IsRecordedInTheTraceOfTheRequest()
    {
        var spans = await SpansOfTheRequestAsync("/cases/manual-current");

        spans.ShouldContain(span => span.Component == CaseComponent && span.Operation == "manual-current");
    }

    [Fact]
    public async Task Step_StartedByATaskThatOutlivesTheRequest_IsNeverAttachedAndNeverOpensATrace()
    {
        var spans = await SpansOfTheRequestAsync("/cases/outliving-task");
        _lateStepFinished.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
        await Task.Delay(200);

        spans.ShouldNotContain(span => span.Operation == "outliving-task");
        _host.Services.GetRequiredService<FlowRecorder>().ActiveTraceCount.ShouldBe(0);
    }

    private async Task<IReadOnlyList<FlowSpan>> SpansOfTheRequestAsync(string path)
    {
        var response = await _client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        FlowTraceDetail? detail = null;
        while (detail is null && DateTime.UtcNow < deadline)
        {
            detail = NetApiFlow.GetRecentTrace(_host.Services, traceId);
            if (detail is null)
            {
                await Task.Delay(25);
            }
        }

        detail.ShouldNotBeNull();
        return detail.Spans;
    }

    private void MapCases(IEndpointRouteBuilder endpoints, string upstreamUrl)
    {
        endpoints.MapGet("/cases/task-run", async () =>
        {
            await Task.Run(() =>
            {
                using var step = NetApiFlow.Step(CaseComponent, "task-run");
            });
            return "ok";
        });

        endpoints.MapGet("/cases/when-all", async () =>
        {
            await Task.WhenAll(Enumerable.Range(0, 3).Select(index => Task.Run(async () =>
            {
                using var step = NetApiFlow.Step(CaseComponent, $"when-all-{index}");
                await Task.Delay(5);
            })));
            return "ok";
        });

        endpoints.MapGet("/cases/async-enumerable", async () =>
        {
            await foreach (var item in Iterate())
            {
                _ = item;
            }

            return "ok";
        });

        endpoints.MapGet("/cases/http-client", async () =>
        {
            using var client = new HttpClient();
            await client.GetStringAsync(upstreamUrl);
            return "ok";
        });

        endpoints.MapGet("/cases/efcore", async (CasesDbContext context) =>
        {
            await context.Items.CountAsync();
            return "ok";
        });

        endpoints.MapGet("/cases/suppress-flow", async () =>
        {
            Task work;
            using (ExecutionContext.SuppressFlow())
            {
                work = Task.Run(() =>
                {
                    using var step = NetApiFlow.Step(CaseComponent, "suppress-flow");
                });
            }

            await work;
            return "ok";
        });

        endpoints.MapGet("/cases/explicit-parent", () =>
        {
            using var activity = ExplicitParentSource.StartActivity("explicit-parent", ActivityKind.Internal, Activity.Current!.Context);
            return "ok";
        });

        endpoints.MapGet("/cases/explicit-parent-without-flow", async () =>
        {
            var context = Activity.Current!.Context;
            Task work;
            using (ExecutionContext.SuppressFlow())
            {
                work = Task.Run(() =>
                {
                    using var activity = ExplicitParentSource.StartActivity("explicit-parent-without-flow", ActivityKind.Internal, context);
                });
            }

            await work;
            return "ok";
        });

        endpoints.MapGet("/cases/manual-current", async () =>
        {
            var captured = Activity.Current;
            Task work;
            using (ExecutionContext.SuppressFlow())
            {
                work = Task.Run(() =>
                {
                    Activity.Current = captured;
                    using var step = NetApiFlow.Step(CaseComponent, "manual-current");
                });
            }

            await work;
            return "ok";
        });

        endpoints.MapGet("/cases/outliving-task", () =>
        {
            _lateStepFinished.Reset();
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                using (NetApiFlow.Step(CaseComponent, "outliving-task"))
                {
                    _lateStepFinished.Set();
                }
            });
            return "ok";
        });
    }

    private static async IAsyncEnumerable<int> Iterate()
    {
        for (var index = 0; index < 2; index++)
        {
            using (NetApiFlow.Step(CaseComponent, $"async-enumerable-{index}"))
            {
                await Task.Delay(2);
            }

            yield return index;
        }
    }

    private string StartUpstream()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var url = $"http://localhost:{port}/";
        _upstream.Prefixes.Add(url);
        _upstream.Start();
        _ = Task.Run(async () =>
        {
            while (_upstream.IsListening)
            {
                try
                {
                    var context = await _upstream.GetContextAsync();
                    context.Response.StatusCode = 200;
                    context.Response.Close();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }
            }
        });
        return url;
    }

    private sealed class CasesDbContext(DbContextOptions<CasesDbContext> options) : DbContext(options)
    {
        public DbSet<CaseItem> Items => Set<CaseItem>();
    }

    private sealed class CaseItem
    {
        public int Id { get; set; }
    }
}
