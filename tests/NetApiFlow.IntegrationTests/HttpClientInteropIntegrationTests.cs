using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class HttpClientInteropIntegrationTests
{
    private static HttpListener StartUpstream(out string baseUrl)
    {
        var port = GetFreeTcpPort();
        baseUrl = $"http://localhost:{port}/";

        var listener = new HttpListener();
        listener.Prefixes.Add(baseUrl);
        listener.Start();

        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                context.Response.StatusCode = context.Request.Url!.AbsolutePath.Contains("fail", StringComparison.Ordinal)
                    ? 500
                    : 200;
                context.Response.Close();
            }
        });

        return listener;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static IHost CreateHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddHttpClient();
                    services.AddNetApiFlow(o =>
                    {
                        o.IncludePaths("/**");
                        o.ExternalSources = ExternalActivitySource.HttpClient;
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/call", async (IHttpClientFactory httpClientFactory, string target) =>
                        {
                            using var serviceStep = NetApiFlow.Step("OrderService", "NotifyAsync", ComponentType.Service);
                            using var client = httpClientFactory.CreateClient();
                            try
                            {
                                using var response = await client.GetAsync($"{target}users/42?token=abc123&email=x@y.com");
                                return Results.StatusCode((int)response.StatusCode);
                            }
                            catch (HttpRequestException)
                            {
                                return Results.StatusCode(StatusCodes.Status500InternalServerError);
                            }
                        });
                    });
                });
            })
            .Start();
    }

    private static List<FlowSpan> CaptureSpans(FlowRecorder recorder, string traceId, ConcurrentDictionary<string, List<FlowSpan>> spansByTrace) =>
        spansByTrace.GetOrAdd(traceId, _ => []);

    [Fact]
    public async Task OutboundCall_WithSuccessfulResponse_ProducesHttpSpanAsChildOfTheActiveStep()
    {
        using var upstream = StartUpstream(out var upstreamUrl);
        using var host = CreateHost();
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = new ConcurrentDictionary<string, List<FlowSpan>>();
        void OnSpan(FlowSpan span) => CaptureSpans(recorder, span.TraceId, spansByTrace).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync($"/api/call?target={Uri.EscapeDataString(upstreamUrl)}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var spans = spansByTrace[traceId];

        var serviceSpan = spans.Single(s => s.Component == "OrderService");
        var httpSpan = spans.Single(s => s.Type == ComponentType.Http);

        httpSpan.Component.ShouldBe("HttpClient");
        httpSpan.ParentSpanId.ShouldBe(serviceSpan.SpanId);
        httpSpan.Status.ShouldBe(FlowStatus.Success);

        var expectedAuthority = new Uri(upstreamUrl).Authority;
        httpSpan.Operation.ShouldBe($"GET {expectedAuthority}/users/42");

        upstream.Stop();
    }

    [Fact]
    public async Task OutboundCall_WithQueryStringContainingSensitiveData_NeverRecordsTheQueryString()
    {
        using var upstream = StartUpstream(out var upstreamUrl);
        using var host = CreateHost();
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = new ConcurrentDictionary<string, List<FlowSpan>>();
        void OnSpan(FlowSpan span) => CaptureSpans(recorder, span.TraceId, spansByTrace).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync($"/api/call?target={Uri.EscapeDataString(upstreamUrl)}");

        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var httpSpan = spansByTrace[traceId].Single(s => s.Type == ComponentType.Http);

        httpSpan.Operation.ShouldNotContain("token");
        httpSpan.Operation.ShouldNotContain("abc123");
        httpSpan.Operation.ShouldNotContain("email");
        httpSpan.Operation.ShouldNotContain("x@y.com");
        httpSpan.Operation.ShouldNotContain("?");

        upstream.Stop();
    }

    [Fact]
    public async Task OutboundCall_WithServerErrorResponse_MarksSpanAsError()
    {
        using var upstream = StartUpstream(out var upstreamUrl);
        using var host = CreateHost();
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = new ConcurrentDictionary<string, List<FlowSpan>>();
        void OnSpan(FlowSpan span) => CaptureSpans(recorder, span.TraceId, spansByTrace).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync($"/api/call?target={Uri.EscapeDataString(upstreamUrl)}fail/");

        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var httpSpan = spansByTrace[traceId].Single(s => s.Type == ComponentType.Http);

        httpSpan.Status.ShouldBe(FlowStatus.Error);
        httpSpan.IsErrorOrigin.ShouldBeFalse();

        upstream.Stop();
    }

    [Fact]
    public async Task OutboundCall_ToUnreachableHost_MarksSpanAsErrorOriginWithoutCrashingTheRequest()
    {
        using var host = CreateHost();
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        var spansByTrace = new ConcurrentDictionary<string, List<FlowSpan>>();
        void OnSpan(FlowSpan span) => CaptureSpans(recorder, span.TraceId, spansByTrace).Add(span);
        recorder.SpanCompleted += OnSpan;
        recorder.SpanFailed += OnSpan;

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync($"/api/call?target={Uri.EscapeDataString("http://localhost:1/")}");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var httpSpan = spansByTrace[traceId].Single(s => s.Type == ComponentType.Http);

        httpSpan.Status.ShouldBe(FlowStatus.Error);
        httpSpan.IsErrorOrigin.ShouldBeTrue();
        httpSpan.ErrorMessage.ShouldNotBeNull();
    }
}
