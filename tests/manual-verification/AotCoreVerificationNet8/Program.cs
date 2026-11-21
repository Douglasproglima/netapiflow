using System.Globalization;
using NetApiFlow;
using NetApiFlow.Domain;

const string VerifyArgument = "--verify-http-backfill";

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddNetApiFlow(options => options.ExternalSources = ExternalActivitySource.HttpClient);

var app = builder.Build();

app.MapGet("/ping", () => "pong");
app.MapGet("/calls-ok", (HttpRequest request) => CallAsync($"{request.Scheme}://{request.Host}/ping"));
app.MapGet("/calls-missing", (HttpRequest request) => CallAsync($"{request.Scheme}://{request.Host}/does-not-exist"));
app.MapGet("/calls-refused", () => CallAsync("http://127.0.0.1:1/refused"));

if (!args.Contains(VerifyArgument))
{
    app.Run();
    return 0;
}

await app.StartAsync();

var address = new Uri(app.Urls.First());
var authority = $"127.0.0.1:{address.Port.ToString(CultureInfo.InvariantCulture)}";

using (var driver = new HttpClient { BaseAddress = address })
{
    foreach (var route in new[] { "/calls-ok", "/calls-missing", "/calls-refused" })
    {
        await driver.GetStringAsync(route);
    }
}

var store = app.Services.GetRequiredService<ITraceStore>();
var expectations = new Expectation[]
{
    new("/calls-ok", $"GET {authority}/ping", FlowStatus.Success),
    new("/calls-missing", $"GET {authority}/does-not-exist", FlowStatus.Error),
    new("/calls-refused", "GET 127.0.0.1:1/refused", FlowStatus.Error),
};

var failures = 0;
foreach (var expectation in expectations)
{
    var recordedTrace = await WaitForTraceAsync(store, expectation.Route);
    var recorded = recordedTrace?.Spans.FirstOrDefault(candidate => candidate.Type == ComponentType.Http);
    var matches = recorded is not null
        && recorded.Operation == expectation.Operation
        && recorded.Status == expectation.Status;

    Console.WriteLine(matches
        ? $"ok: {expectation.Route} recorded '{recorded!.Operation}' as {recorded.Status}"
        : $"FAILED: {expectation.Route} expected '{expectation.Operation}' as {expectation.Status}, recorded '{recorded?.Operation ?? "no HttpClient span"}' as {recorded?.Status.ToString() ?? "n/a"}");

    failures += matches ? 0 : 1;

    var hasEndpointStep = recordedTrace?.Spans.Any(candidate => candidate.Type == ComponentType.Endpoint) == true;
    Console.WriteLine(hasEndpointStep
        ? $"ok: {expectation.Route} recorded an Endpoint step without marking"
        : $"FAILED: {expectation.Route} recorded no Endpoint step");
    failures += hasEndpointStep ? 0 : 1;
}

await app.StopAsync();
return failures == 0 ? 0 : 1;

static async Task<string> CallAsync(string url)
{
    using var client = new HttpClient();
    try
    {
        var response = await client.GetAsync(url);
        return ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
    }
    catch (HttpRequestException)
    {
        return "connection failed";
    }
}

static async Task<FlowTraceDetail?> WaitForTraceAsync(ITraceStore store, string route)
{
    var deadline = DateTime.UtcNow.AddSeconds(15);
    while (DateTime.UtcNow < deadline)
    {
        var page = await store.QueryAsync(new TraceQuery(), CancellationToken.None);
        var summary = page.Items.FirstOrDefault(item => item.RouteTemplate == route);
        if (summary is not null)
        {
            var detail = await store.GetAsync(summary.TraceId, CancellationToken.None);
            if (detail?.Spans.Any(candidate => candidate.Type == ComponentType.Http) == true)
            {
                return detail;
            }
        }

        await Task.Delay(250);
    }

    return null;
}

internal sealed record Expectation(string Route, string Operation, FlowStatus Status);
