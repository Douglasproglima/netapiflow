using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetApiFlow.Domain;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class NullStepArgumentIntegrationTests
{
    private const string TraceIdHeader = "X-NetApiFlow-TraceId";
    private const string Answer = "answered";

    [Theory]
    [InlineData("/step-with-a-null-component")]
    [InlineData("/step-with-a-null-operation")]
    public async Task Step_WithANullArgument_AnswersTheRequestRecordsNoStepAndLogsAWarning(string path)
    {
        var logs = new CapturingLoggerProvider();
        using var host = await StartHostAsync(logs);

        var response = await host.GetTestServer().CreateClient().GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe(Answer);
        (await SpansOfAsync(host, response)).ShouldNotContain(span => span.Type == ComponentType.Service);
        logs.Warnings.ShouldContain(message => message.Contains("null", StringComparison.Ordinal) && message.Contains("step", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/tag-with-a-null-key")]
    [InlineData("/tag-with-a-null-value")]
    public async Task Tag_WithANullArgument_AnswersTheRequestKeepsTheStepWithoutTheTagAndLogsAWarning(string path)
    {
        var logs = new CapturingLoggerProvider();
        using var host = await StartHostAsync(logs);

        var response = await host.GetTestServer().CreateClient().GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe(Answer);
        var step = (await SpansOfAsync(host, response)).Single(span => span.Type == ComponentType.Service);
        step.Tags.ContainsKey("kept-out").ShouldBeFalse();
        step.Tags["kept"].ShouldBe("yes");
        logs.Warnings.ShouldContain(message => message.Contains("null", StringComparison.Ordinal) && message.Contains("tag", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MarkError_WithANullException_AnswersTheRequestKeepsTheStepSuccessfulAndLogsAWarning()
    {
        var logs = new CapturingLoggerProvider();
        using var host = await StartHostAsync(logs);

        var response = await host.GetTestServer().CreateClient().GetAsync("/mark-error-with-null");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe(Answer);
        var step = (await SpansOfAsync(host, response)).Single(span => span.Type == ComponentType.Service);
        step.Status.ShouldBe(FlowStatus.Success);
        logs.Warnings.ShouldContain(message => message.Contains("null", StringComparison.Ordinal) && message.Contains("error", StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyList<FlowSpan>> SpansOfAsync(IHost host, HttpResponseMessage response)
    {
        var traceId = response.Headers.GetValues(TraceIdHeader).Single();
        var trace = await host.Services.GetRequiredService<ITraceStore>().WaitForPersistedTraceAsync(traceId);
        return trace.Spans;
    }

    private static async Task<IHost> StartHostAsync(CapturingLoggerProvider logs)
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.AddProvider(logs))
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/step-with-a-null-component", () =>
                        {
                            using (Flow.Step(null!, "PlaceAsync", ComponentType.Service))
                            {
                                return Answer;
                            }
                        });
                        endpoints.MapGet("/step-with-a-null-operation", () =>
                        {
                            using (Flow.Step("OrderService", null!, ComponentType.Service))
                            {
                                return Answer;
                            }
                        });
                        endpoints.MapGet("/tag-with-a-null-key", () =>
                        {
                            using var step = Flow.Step("OrderService", "PlaceAsync", ComponentType.Service);
                            step.Tag("kept", "yes");
                            step.Tag(null!, "kept-out");
                            return Answer;
                        });
                        endpoints.MapGet("/tag-with-a-null-value", () =>
                        {
                            using var step = Flow.Step("OrderService", "PlaceAsync", ComponentType.Service);
                            step.Tag("kept", "yes");
                            step.Tag("kept-out", null!);
                            return Answer;
                        });
                        endpoints.MapGet("/mark-error-with-null", () =>
                        {
                            using var step = Flow.Step("OrderService", "PlaceAsync", ComponentType.Service);
                            step.MarkError(null!);
                            return Answer;
                        });
                    });
                });
            })
            .StartAsync();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _warnings = new();

        public IReadOnlyList<string> Warnings => [.. _warnings];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_warnings);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> warnings) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                {
                    warnings.Enqueue(formatter(state, exception));
                }
            }
        }
    }
}
