using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

/// <summary>
/// Verifies tail-based sampling (D-82, tarefa 15.3): the decision only affects what reaches the
/// persisted history, never the live view.
/// </summary>
public class TailSamplingIntegrationTests
{
    private static IHost CreateHost(Action<NetApiFlowOptions> configure)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow(configure);
                    services.AddNetApiFlowDashboard();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapNetApiFlowDashboardApi());
                });
            })
            .Start();
    }

    private static async Task<HashSet<string>> WaitForTraceIdsAsync(HttpClient client, int expectedCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        HashSet<string> traceIds;
        do
        {
            var response = await client.GetAsync("/flow/api/traces?pageSize=100");
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            traceIds = doc.RootElement.GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("traceId").GetString()!)
                .ToHashSet();

            if (traceIds.Count >= expectedCount)
            {
                break;
            }

            await Task.Delay(20);
        }
        while (DateTime.UtcNow < deadline);

        return traceIds;
    }

    [Fact]
    public async Task GetTraces_WithTailSamplingEnabled_PersistsErrorsAndSlowTracesButDropsFastSuccess()
    {
        using var host = CreateHost(options =>
        {
            options.SlowThreshold = TimeSpan.FromMilliseconds(1);
            options.TailSampling.Enabled = true;
            options.TailSampling.SuccessSampleRate = 0.0;
        });
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var client = host.GetTestServer().CreateClient();

        recorder.TryStartTrace("ts-error", "GET", "/api/orders", DateTime.UtcNow);
        recorder.CompleteTrace("ts-error", 500, FlowStatus.Error, "boom");

        recorder.TryStartTrace("ts-slow", "GET", "/api/orders", DateTime.UtcNow.AddMilliseconds(-10));
        recorder.CompleteTrace("ts-slow", 200, FlowStatus.Success, null);

        recorder.TryStartTrace("ts-fast-success", "GET", "/api/orders", DateTime.UtcNow);
        recorder.CompleteTrace("ts-fast-success", 200, FlowStatus.Success, null);

        var traceIds = await WaitForTraceIdsAsync(client, expectedCount: 2, TimeSpan.FromSeconds(5));

        traceIds.ShouldBe(["ts-error", "ts-slow"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetTracesLive_WhenTailSamplingWouldDropTheTrace_StillShowsItWhileRunning()
    {
        using var host = CreateHost(options =>
        {
            options.TailSampling.Enabled = true;
            options.TailSampling.SuccessSampleRate = 0.0;
        });
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var client = host.GetTestServer().CreateClient();

        recorder.TryStartTrace("ts-live-fast", "GET", "/api/orders", DateTime.UtcNow);

        var response = await client.GetAsync("/flow/api/traces/live");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.EnumerateArray().Select(e => e.GetProperty("traceId").GetString()).ShouldContain("ts-live-fast");
    }

    [Fact]
    public async Task Stream_WhenTailSamplingWouldDropTheTrace_StillDeliversAllLiveEvents()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseKestrel();
                webBuilder.UseUrls("http://127.0.0.1:0");
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow(options =>
                    {
                        options.IncludePaths("/api/**");
                        options.TailSampling.Enabled = true;
                        options.TailSampling.SuccessSampleRate = 0.0;
                    });
                    services.AddNetApiFlowDashboard();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/ping", () => "pong").WithNetApiFlowTracking();
                        endpoints.MapNetApiFlowDashboardApi();
                    });
                });
            })
            .Build();
        await host.StartAsync();
        using var _host = host;

        var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        using var client = new HttpClient { BaseAddress = new Uri(addresses.Single()) };

        using var request = new HttpRequestMessage(HttpMethod.Get, "/flow/api/stream");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var connectEvent = await ReadEventTypeAsync(reader);
        connectEvent.ShouldBe("ping");

        var pingResponse = await client.GetAsync("/api/ping");
        pingResponse.EnsureSuccessStatusCode();

        var eventTypes = new List<string>
        {
            await ReadEventTypeAsync(reader),
            await ReadEventTypeAsync(reader),
            await ReadEventTypeAsync(reader),
            await ReadEventTypeAsync(reader),
        };

        eventTypes.ShouldBe(["TraceStarted", "SpanStarted", "SpanCompleted", "TraceCompleted"]);
    }

    private static async Task<string> ReadEventTypeAsync(StreamReader reader)
    {
        string? eventType = null;
        var sawData = false;

        string? line;
        while ((line = await ReadLineWithTimeoutAsync(reader)) is not null)
        {
            if (line.Length == 0)
            {
                if (sawData)
                {
                    break;
                }

                continue;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventType = line[7..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                sawData = true;
            }
        }

        return eventType!;
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var lineTask = reader.ReadLineAsync(cts.Token).AsTask();
        var completed = await Task.WhenAny(lineTask, Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None));
        return completed == lineTask ? await lineTask : throw new TimeoutException("Timed out waiting for an SSE line.");
    }
}
