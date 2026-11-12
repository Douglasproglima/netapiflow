#if NET8_0
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Instrumentation;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Instrumentation;

public class HttpClientActivityTagBackfillTests
{
    [Fact]
    public async Task BackfillRequestTags_WithQueryStringContainingSensitiveData_NeverWritesItOntoTheSharedActivity()
    {
        var options = Options.Create(new NetApiFlowOptions { ExternalSources = ExternalActivitySource.HttpClient });
        using var subject = new HttpClientActivityTagBackfill(options, NullLogger<HttpClientActivityTagBackfill>.Instance);

        var capturedTags = new List<KeyValuePair<string, object?>>();
        var externalListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == HttpClientActivityTagNames.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => capturedTags.AddRange(activity.TagObjects),
        };
        ActivitySource.AddActivityListener(externalListener);
        using var _ = externalListener;

        using var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:28936/");
        listener.Start();
        using var _2 = listener;
        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = 200;
            context.Response.Close();
        });

        using var client = new HttpClient();
        await client.GetAsync("http://localhost:28936/users/42?token=abc123&email=x@y.com");
        await serverTask;

        var urlFull = capturedTags.Single(t => t.Key == HttpClientActivityTagNames.UrlFull).Value as string;
        urlFull.ShouldNotBeNull();
        urlFull.ShouldNotContain("token");
        urlFull.ShouldNotContain("email");
        urlFull.ShouldNotContain("?");
    }
}
#endif
