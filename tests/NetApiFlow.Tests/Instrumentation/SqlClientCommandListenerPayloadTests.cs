using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetApiFlow.Instrumentation;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Instrumentation;

/// <summary>
/// Microsoft.Data.SqlClient only reports its command events as a key/value list from 6.0 on.
/// Against an older driver the interop records nothing at all, so it has to say so instead of
/// leaving the user with an enabled flag and an empty flow.
/// </summary>
public class SqlClientCommandListenerPayloadTests
{
    private sealed record UnreadablePayload(Guid OperationId);

    private sealed class CapturingLogger : ILogger<SqlClientCommandListener>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static SqlClientCommandListener CreateSubject(CapturingLogger logger) =>
        new(
            Options.Create(new NetApiFlowOptions { ExternalSources = ExternalActivitySource.SqlClient }),
            logger);

    private static KeyValuePair<string, object?> UnreadableEvent() =>
        new(SqlClientDiagnosticNames.CommandBefore, new UnreadablePayload(Guid.NewGuid()));

    [Fact]
    public void OnNext_WithAPayloadItCannotRead_SaysSoOnceAndNamesTheType()
    {
        var logger = new CapturingLogger();
        using var subject = CreateSubject(logger);

        subject.OnNext(UnreadableEvent());

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldContain(typeof(UnreadablePayload).FullName!);
        entry.Message.ShouldContain("6.0 or later");
    }

    [Fact]
    public void OnNext_WithManyUnreadablePayloads_LogsOnlyOnce()
    {
        var logger = new CapturingLogger();
        using var subject = CreateSubject(logger);

        for (var i = 0; i < 50; i++)
        {
            subject.OnNext(UnreadableEvent());
        }

        logger.Entries.Count.ShouldBe(1);
    }

    [Fact]
    public void OnNext_AfterDispose_StaysSilentEvenAboutAnUnreadablePayload()
    {
        var logger = new CapturingLogger();
        var subject = CreateSubject(logger);

        subject.Dispose();
        subject.OnNext(UnreadableEvent());

        logger.Entries.ShouldBeEmpty();
    }
}
