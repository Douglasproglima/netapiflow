using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.OpenTelemetry;
using NetApiFlow.TestSupport;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Shouldly;

namespace NetApiFlow.OpenTelemetry.Tests;

public class ErrorMessageExportTests(ErrorMessagesExportedForEveryCase exported)
    : IClassFixture<ErrorMessagesExportedForEveryCase>
{
    [Theory]
    [MemberData(nameof(SecretLeakCases.Ids), MemberType = typeof(SecretLeakCases))]
    public void MarkError_WithASecretInTheMessage_ExportsNoFragmentOfItAndNoMoreThan300Characters(int id)
    {
        var leakCase = SecretLeakCases.ById(id);

        var messages = exported.MessagesOf(id);

        messages.ShouldNotBeEmpty();
        foreach (var fragment in leakCase.SecretFragments)
        {
            messages.ShouldAllBe(message => !message.Contains(fragment, StringComparison.Ordinal));
        }

        messages.ShouldAllBe(message => message.Length <= SecretLeakCases.MaxOutputLength);
    }
}

public sealed class ErrorMessagesExportedForEveryCase : IAsyncLifetime
{
    private const string ErrorMessageTag = "netapiflow.error.message";
    private const string TraceIdHeader = "X-NetApiFlow-TraceId";

    private readonly Dictionary<int, IReadOnlyList<string>> _messagesByCase = [];
    private IHost? _host;

    public IReadOnlyList<string> MessagesOf(int id) => _messagesByCase[id];

    public async Task InitializeAsync()
    {
        var exported = new List<Activity>();
        _host = await StartHostAsync(exported);
        var client = _host.GetTestServer().CreateClient();

        var traceIdByCase = new Dictionary<int, string>();
        foreach (var leakCase in SecretLeakCases.All)
        {
            var response = await client.GetAsync($"/boom/{leakCase.Id}");
            traceIdByCase[leakCase.Id] = response.Headers.GetValues(TraceIdHeader).Single();
        }

        _host.Services.GetRequiredService<TracerProvider>().ForceFlush();

        foreach (var (id, traceId) in traceIdByCase)
        {
            _messagesByCase[id] = exported
                .Where(activity => activity.TraceId.ToString() == traceId)
                .Select(activity => activity.GetTagItem(ErrorMessageTag) as string)
                .OfType<string>()
                .ToList();
        }
    }

    public Task DisposeAsync()
    {
        _host?.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<IHost> StartHostAsync(List<Activity> exported) =>
        await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow();
                    services.AddNetApiFlowOtlpExport();
                    services.ConfigureOpenTelemetryTracerProvider(builder => builder.AddInMemoryExporter(exported));
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
                            .MapGet("/boom/{id:int}", string (int id) => throw new InvalidOperationException(SecretLeakCases.ById(id).Message))
                            .WithNetApiFlowTracking();
                    });
                });
            })
            .StartAsync();
}
