using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class InstrumentationFailureIntegrationTests
{
    private const string NotAffectedMessage = "The request was not affected";

    [Fact]
    public async Task Request_WhenRecordingTheStartOfAStepThrows_RespondsNormallyAndLogsTheFailure()
    {
        var logs = new CapturingLoggerProvider();
        using var host = await StartHostAsync(logs);
        var failure = new InvalidOperationException("a recorder subscriber failed on SpanStarted");
        host.Services.GetRequiredService<FlowRecorder>().SpanStarted += _ => throw failure;

        var response = await host.GetTestServer().CreateClient().GetAsync("/step");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("step done");
        logs.ShouldHaveLogged(failure, NotAffectedMessage);
    }

    [Fact]
    public async Task Request_WhenAStepCarriesATagValueThatBreaksTheRecording_RespondsNormallyAndLogsTheFailure()
    {
        var logs = new CapturingLoggerProvider();
        using var host = await StartHostAsync(logs);

        var response = await host.GetTestServer().CreateClient().GetAsync("/step-with-a-bool-error-origin");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("step done");
        logs.ShouldHaveLogged<InvalidCastException>(NotAffectedMessage);
    }

    [Fact]
    public async Task Request_WhenCompletingTheTraceThrows_RespondsNormallyAndLogsTheFailure()
    {
        var logs = new CapturingLoggerProvider();
        using var host = await StartHostAsync(logs);
        var failure = new InvalidOperationException("a recorder subscriber failed on TraceCompleted");
        host.Services.GetRequiredService<FlowRecorder>().TraceCompleted += _ => throw failure;

        var response = await host.GetTestServer().CreateClient().GetAsync("/ping");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("pong");
        logs.ShouldHaveLogged(failure, NotAffectedMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Start_WithAnInvalidIncludePathPattern_FailsWithAMessageThatNamesTheOption(string? pattern)
    {
        var exception = await Should.ThrowAsync<OptionsValidationException>(
            () => StartHostAsync(new CapturingLoggerProvider(), options => options.IncludePaths("/api/**", pattern!)));

        exception.Message.ShouldContain("IncludePaths");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Start_WithAnInvalidExcludePathPattern_FailsWithAMessageThatNamesTheOption(string? pattern)
    {
        var exception = await Should.ThrowAsync<OptionsValidationException>(
            () => StartHostAsync(new CapturingLoggerProvider(), options => options.ExcludePaths("/health/**", pattern!)));

        exception.Message.ShouldContain("ExcludePaths");
    }

    private static async Task<IHost> StartHostAsync(CapturingLoggerProvider logs, Action<NetApiFlowOptions>? configure = null)
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
                    services.AddNetApiFlow(configure);
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/ping", () => "pong");
                        endpoints.MapGet("/step", () =>
                        {
                            using (Flow.Step("OrderService", "PlaceAsync", ComponentType.Service))
                            {
                                return "step done";
                            }
                        });
                        endpoints.MapGet("/step-with-a-bool-error-origin", () =>
                        {
                            using (Flow.Step("OrderService", "PlaceAsync", ComponentType.Service))
                            {
                                Activity.Current!.SetTag(FlowTagNames.ErrorOrigin, true);
                                return "step done";
                            }
                        });
                    });
                });
            })
            .StartAsync();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(string Message, Exception? Exception)> _entries = new();

        public void ShouldHaveLogged(Exception failure, string messagePart) =>
            _entries.ShouldContain(entry => ReferenceEquals(entry.Exception, failure) && entry.Message.Contains(messagePart, StringComparison.Ordinal));

        public void ShouldHaveLogged<TException>(string messagePart)
            where TException : Exception =>
            _entries.ShouldContain(entry => entry.Exception is TException && entry.Message.Contains(messagePart, StringComparison.Ordinal));

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<(string Message, Exception? Exception)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((formatter(state, exception), exception));
        }
    }
}
