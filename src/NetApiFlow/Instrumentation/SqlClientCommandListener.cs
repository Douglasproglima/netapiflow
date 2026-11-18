using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetApiFlow.Instrumentation;

/// <summary>
/// Opens a <see cref="ComponentType.Database"/> step per SQL command issued through
/// Microsoft.Data.SqlClient, so data access that bypasses EF Core (Dapper, a raw
/// <see cref="DbCommand"/>) still shows up in the flow.
/// <para>The driver publishes a <see cref="DiagnosticListener"/>, never an
/// <see cref="ActivitySource"/>, which is why this cannot reuse the Npgsql path. The event payload
/// is read through <see cref="IReadOnlyList{T}"/> of <see cref="KeyValuePair{TKey, TValue}"/>, and
/// the command through <see cref="DbCommand"/>: both come from the base framework, so the core
/// package reads the payload strongly typed without referencing the driver and without reflection.
/// A driver too old to expose its payload that way (5.x writes an anonymous type) simply produces
/// no step, rather than failing.</para>
/// </summary>
internal sealed partial class SqlClientCommandListener
    : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly ConcurrentDictionary<Guid, FlowStep> _pendingSteps = new();
    private readonly IOptions<NetApiFlowOptions> _options;
    private readonly ILogger<SqlClientCommandListener> _logger;
    private readonly IDisposable _allListenersSubscription;
    private IDisposable? _listenerSubscription;
    private volatile bool _disposed;

    public SqlClientCommandListener(IOptions<NetApiFlowOptions> options, ILogger<SqlClientCommandListener> logger)
    {
        _options = options;
        _logger = logger;
        _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
    }

    public void Dispose()
    {
        _disposed = true;
        _listenerSubscription?.Dispose();
        _allListenersSubscription.Dispose();
    }

    public void OnNext(DiagnosticListener value)
    {
        if (value.Name == SqlClientDiagnosticNames.ListenerName)
        {
            _listenerSubscription = value.Subscribe(this);
        }
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        try
        {
            if (_disposed)
            {
                return;
            }

            if (value.Value is not IReadOnlyList<KeyValuePair<string, object>> payload)
            {
                WarnOnceAboutUnreadablePayload(value.Value);
                return;
            }

            switch (value.Key)
            {
                case SqlClientDiagnosticNames.CommandBefore:
                    OnCommandBefore(payload);
                    break;
                case SqlClientDiagnosticNames.CommandAfter:
                    OnCommandCompleted(payload, exception: null);
                    break;
                case SqlClientDiagnosticNames.CommandError:
                    OnCommandCompleted(payload, Find(payload, SqlClientDiagnosticNames.ExceptionKey) as Exception);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.EventProcessingFailed(_logger, value.Key, ex);
        }
    }

    /// <summary>
    /// Logged once per process, not per command: a driver whose payload cannot be read produces
    /// no span at all, and the only thing worse than that is it happening silently. Older
    /// Microsoft.Data.SqlClient releases write an anonymous type here instead of a key/value list.
    /// </summary>
    private void WarnOnceAboutUnreadablePayload(object? payload)
    {
        if (Interlocked.Exchange(ref _unreadablePayloadReported, 1) != 0)
        {
            return;
        }

        Log.PayloadNotReadable(_logger, payload?.GetType().FullName ?? "null");
    }

    private int _unreadablePayloadReported;

    public void OnError(Exception error)
    {
    }

    public void OnCompleted()
    {
    }

    private void OnCommandBefore(IReadOnlyList<KeyValuePair<string, object>> payload)
    {
        if (!_options.Value.ExternalSources.HasFlag(ExternalActivitySource.SqlClient) || IsAlreadyCoveredByAStep())
        {
            return;
        }

        if (Find(payload, SqlClientDiagnosticNames.OperationIdKey) is not Guid operationId ||
            Find(payload, SqlClientDiagnosticNames.CommandKey) is not DbCommand command)
        {
            return;
        }

        var step = NetApiFlow.Step(
            SqlClientDiagnosticNames.Component,
            SqlCommandDescriber.Describe(command.CommandText),
            ComponentType.Database);

        if (step.IsNoOp)
        {
            return;
        }

        if (_options.Value.CaptureSqlText)
        {
            step.Tag("sql", command.CommandText);
        }

        _pendingSteps[operationId] = step;
    }

    private void OnCommandCompleted(IReadOnlyList<KeyValuePair<string, object>> payload, Exception? exception)
    {
        if (Find(payload, SqlClientDiagnosticNames.OperationIdKey) is not Guid operationId ||
            !_pendingSteps.TryRemove(operationId, out var step))
        {
            return;
        }

        if (exception is not null)
        {
            step.MarkError(exception);
        }

        step.Dispose();
    }

    /// <summary>
    /// A NetApiFlow step is already open around this command when <c>NetApiFlow.EntityFrameworkCore</c>'s
    /// interceptor opened one, so recording a second would double every EF Core query. Same rule the
    /// Npgsql path applies, checked here against the ambient step because the driver creates no
    /// activity of its own to compare parents with.
    /// </summary>
    private static bool IsAlreadyCoveredByAStep() =>
        Activity.Current?.Source.Name == FlowActivitySource.Name;

    private static object? Find(IReadOnlyList<KeyValuePair<string, object>> payload, string key)
    {
        for (var i = 0; i < payload.Count; i++)
        {
            if (payload[i].Key == key)
            {
                return payload[i].Value;
            }
        }

        return null;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "NetApiFlow: SqlClient event processing failed for {DiagnosticEvent}")]
        public static partial void EventProcessingFailed(ILogger logger, string diagnosticEvent, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "NetApiFlow: ExternalActivitySource.SqlClient is enabled, but this Microsoft.Data.SqlClient build reports its command events as {PayloadType}, which NetApiFlow cannot read. No Database span will be recorded for SqlClient commands. Upgrade Microsoft.Data.SqlClient to 6.0 or later to enable it.")]
        public static partial void PayloadNotReadable(ILogger logger, string payloadType);
    }
}
