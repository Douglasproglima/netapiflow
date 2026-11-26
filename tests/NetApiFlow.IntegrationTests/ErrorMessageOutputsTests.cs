using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.TestSupport;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class ErrorMessageOutputsTests(ErrorMessagesOnEveryOutput outputs) : IClassFixture<ErrorMessagesOnEveryOutput>
{
    [Theory]
    [MemberData(nameof(SecretLeakCases.Ids), MemberType = typeof(SecretLeakCases))]
    public void PanelRest_WithASecretInTheMessage_ServesNoFragmentOfItAndNoLongerMessage(int id)
    {
        var restJson = outputs.Of(id).RestJson;

        restJson.ShouldContain("\"errorMessage\"");
        AssertCleanJson(id, restJson);
    }

    [Theory]
    [MemberData(nameof(SecretLeakCases.Ids), MemberType = typeof(SecretLeakCases))]
    public void PanelSse_WithASecretInTheMessage_StreamsNoFragmentOfItAndNoLongerMessage(int id)
    {
        var events = outputs.Of(id).SseEvents;

        events.ShouldNotBeEmpty();
        foreach (var eventJson in events)
        {
            AssertCleanJson(id, eventJson);
        }
    }

    [Theory]
    [MemberData(nameof(SecretLeakCases.Ids), MemberType = typeof(SecretLeakCases))]
    public void Storage_WithASecretInTheMessage_PersistsNoFragmentOfItAndNoLongerMessage(int id)
    {
        var storedJson = outputs.Of(id).StoredJson;

        storedJson.ShouldContain("\"ErrorMessage\"");
        AssertCleanJson(id, storedJson);
    }

    [Theory]
    [MemberData(nameof(SecretLeakCases.Ids), MemberType = typeof(SecretLeakCases))]
    public void CorrelatedLog_WithASecretInTheLoggedMessage_ReachesTheSpanWithNoFragmentOfIt(int id)
    {
        var logs = outputs.Of(id).CorrelatedLogMessages;

        logs.ShouldNotBeEmpty();
        foreach (var log in logs)
        {
            AssertCleanText(id, log);
        }
    }

    [Theory]
    [MemberData(nameof(SecretLeakCases.Ids), MemberType = typeof(SecretLeakCases))]
    public void Activity_WithASecretInTheMessage_CarriesTheErrorMessageTagAlreadyClean(int id)
    {
        var messages = outputs.Of(id).ActivityErrorMessages;

        messages.ShouldNotBeEmpty();
        foreach (var message in messages)
        {
            AssertCleanText(id, message);
        }
    }

    private static void AssertCleanText(int id, string text)
    {
        foreach (var fragment in SecretLeakCases.ById(id).SecretFragments)
        {
            text.ShouldNotContain(fragment);
        }

        text.Length.ShouldBeLessThanOrEqualTo(SecretLeakCases.MaxOutputLength);
    }

    private static void AssertCleanJson(int id, string json)
    {
        foreach (var fragment in SecretLeakCases.ById(id).SecretFragments)
        {
            json.ShouldNotContain(fragment);
        }

        using var document = JsonDocument.Parse(json);
        foreach (var message in MessageFieldsOf(document.RootElement))
        {
            message.Length.ShouldBeLessThanOrEqualTo(SecretLeakCases.MaxOutputLength);
        }
    }

    private static IEnumerable<string> MessageFieldsOf(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String && IsMessageField(property.Name))
                    {
                        yield return property.Value.GetString() ?? string.Empty;
                    }

                    foreach (var nested in MessageFieldsOf(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in MessageFieldsOf(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private static bool IsMessageField(string name) =>
        name.Equals("errorMessage", StringComparison.OrdinalIgnoreCase)
        || name.Equals("message", StringComparison.OrdinalIgnoreCase)
        || name.Equals("exceptionMessage", StringComparison.OrdinalIgnoreCase);
}

public sealed record OutputsOfOneCase(
    string RestJson,
    string StoredJson,
    IReadOnlyList<string> SseEvents,
    IReadOnlyList<string> CorrelatedLogMessages,
    IReadOnlyList<string> ActivityErrorMessages);

public sealed partial class ErrorMessagesOnEveryOutput : IAsyncLifetime, IDisposable
{
    private const string ErrorMessageTag = "netapiflow.error.message";
    private const string TraceIdTag = "netapiflow.trace_id";
    private const string TraceIdHeader = "X-NetApiFlow-TraceId";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Dictionary<int, OutputsOfOneCase> _outputsByCase = [];
    private readonly List<string> _sseDataLines = [];
    private readonly Dictionary<string, List<string>> _activityMessagesByTrace = [];
    private readonly object _gate = new();
    private IHost? _host;
    private HttpClient? _client;
    private HttpClient? _streamClient;
    private ActivityListener? _activityListener;
    private CancellationTokenSource? _streamCancellation;

    public OutputsOfOneCase Of(int id) => _outputsByCase[id];

    public async Task InitializeAsync()
    {
        _host = await StartHostAsync();
        var address = _host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address) };
        _streamClient = new HttpClient { BaseAddress = new Uri(address), Timeout = Timeout.InfiniteTimeSpan };
        _activityListener = ListenToErrorMessageTags();
        _streamCancellation = new CancellationTokenSource();
        await ConnectToTheStreamAsync(_streamCancellation.Token);

        var store = _host.Services.GetRequiredService<ITraceStore>();
        var traceIdByCase = new Dictionary<int, string>();
        foreach (var leakCase in SecretLeakCases.All)
        {
            var response = await _client.GetAsync($"/boom/{leakCase.Id}");
            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            traceIdByCase[leakCase.Id] = response.Headers.GetValues(TraceIdHeader).Single();
        }

        foreach (var (id, traceId) in traceIdByCase)
        {
            var stored = await store.WaitForPersistedTraceAsync(traceId);
            var restJson = await _client.GetStringAsync($"/flow/api/traces/{traceId}");
            _outputsByCase[id] = new OutputsOfOneCase(
                restJson,
                JsonSerializer.Serialize(stored),
                await WaitForStreamEventsOfAsync(traceId),
                CorrelatedLogMessagesIn(restJson),
                ActivityMessagesOf(traceId));
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _streamCancellation?.Cancel();
        _streamCancellation?.Dispose();
        _activityListener?.Dispose();
        _client?.Dispose();
        _streamClient?.Dispose();
        _host?.Dispose();
    }

    private static async Task<IHost> StartHostAsync()
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
                        options.IncludePaths("/boom/**");
                        options.LogCorrelation.Enabled = true;
                        options.LogCorrelation.MinLevel = LogLevel.Warning;
                    });
                    services.AddNetApiFlowDashboard();
                });
                webBuilder.Configure(app =>
                {
                    app.UseExceptionHandler(handler => handler.Run(context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        return Task.CompletedTask;
                    }));
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints
                            .MapGet("/boom/{id:int}", string (int id, ILoggerFactory loggerFactory) =>
                            {
                                var message = SecretLeakCases.ById(id).Message;
                                var exception = new InvalidOperationException(message);
                                Log.Failed(loggerFactory.CreateLogger("Boom"), exception, message);
                                throw exception;
                            })
                            .WithNetApiFlowTracking();
                        endpoints.MapNetApiFlowDashboardApi();
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    private ActivityListener ListenToErrorMessageTags()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetApiFlow",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem(ErrorMessageTag) is string message)
                {
                    lock (_gate)
                    {
                        var traceId = (string)activity.GetTagItem(TraceIdTag)!;
                        if (!_activityMessagesByTrace.TryGetValue(traceId, out var messages))
                        {
                            _activityMessagesByTrace[traceId] = messages = [];
                        }

                        messages.Add(message);
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private async Task ConnectToTheStreamAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/flow/api/stream");
        var response = await _streamClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));

        _ = Task.Run(
            async () =>
            {
                try
                {
                    while (await reader.ReadLineAsync(cancellationToken) is { } line)
                    {
                        if (line.StartsWith("data: ", StringComparison.Ordinal))
                        {
                            lock (_gate)
                            {
                                _sseDataLines.Add(line[6..]);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (IOException)
                {
                }
            },
            CancellationToken.None);

        await WaitUntilAsync(() =>
        {
            lock (_gate)
            {
                return _sseDataLines.Count > 0;
            }
        });
    }

    private async Task<IReadOnlyList<string>> WaitForStreamEventsOfAsync(string traceId)
    {
        await WaitUntilAsync(() => StreamEventsOf(traceId).Any(line => line.Contains("TracePersisted", StringComparison.Ordinal)));
        return StreamEventsOf(traceId);
    }

    private List<string> StreamEventsOf(string traceId)
    {
        lock (_gate)
        {
            return _sseDataLines.Where(line => line.Contains(traceId, StringComparison.Ordinal)).ToList();
        }
    }

    private IReadOnlyList<string> ActivityMessagesOf(string traceId)
    {
        lock (_gate)
        {
            return _activityMessagesByTrace.TryGetValue(traceId, out var messages) ? [.. messages] : [];
        }
    }

    private static List<string> CorrelatedLogMessagesIn(string restJson)
    {
        using var document = JsonDocument.Parse(restJson);
        var messages = new List<string>();
        foreach (var span in document.RootElement.GetProperty("spans").EnumerateArray())
        {
            foreach (var log in span.GetProperty("logs").EnumerateArray())
            {
                messages.Add(log.GetProperty("message").GetString() ?? string.Empty);
                if (log.TryGetProperty("exceptionMessage", out var exceptionMessage) && exceptionMessage.ValueKind == JsonValueKind.String)
                {
                    messages.Add(exceptionMessage.GetString() ?? string.Empty);
                }
            }
        }

        return messages;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed with {Message}")]
        public static partial void Failed(ILogger logger, Exception exception, string message);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The expected output never arrived.");
            }

            await Task.Delay(20);
        }
    }
}
