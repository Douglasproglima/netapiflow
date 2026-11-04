#if NET8_0
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetApiFlow.Instrumentation;

internal sealed partial class HttpClientActivityTagBackfill
    : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly IOptions<NetApiFlowOptions> _options;
    private readonly ILogger<HttpClientActivityTagBackfill> _logger;
    private readonly IDisposable _allListenersSubscription;
    private IDisposable? _httpDiagnosticsSubscription;

    public HttpClientActivityTagBackfill(IOptions<NetApiFlowOptions> options, ILogger<HttpClientActivityTagBackfill> logger)
    {
        _options = options;
        _logger = logger;
        _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
    }

    public void Dispose()
    {
        _httpDiagnosticsSubscription?.Dispose();
        _allListenersSubscription.Dispose();
    }

    public void OnNext(DiagnosticListener value)
    {
        if (value.Name == "HttpHandlerDiagnosticListener")
        {
            _httpDiagnosticsSubscription = value.Subscribe(this);
        }
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (!_options.Value.ExternalSources.HasFlag(ExternalActivitySource.HttpClient))
        {
            return;
        }

        try
        {
            switch (value.Key)
            {
                case "System.Net.Http.HttpRequestOut.Start":
                    BackfillRequestTags(value.Value);
                    break;
                case "System.Net.Http.HttpRequestOut.Stop":
                    BackfillResponseTags(value.Value);
                    break;
                case "System.Net.Http.Exception":
                    BackfillErrorTag(value.Value);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.BackfillFailed(_logger, value.Key, ex);
        }
    }

    public void OnError(Exception error)
    {
    }

    public void OnCompleted()
    {
    }

    private static void BackfillRequestTags(object? payload)
    {
        if (payload?.GetType().GetProperty("Request")?.GetValue(payload) is not HttpRequestMessage request)
        {
            return;
        }

        Activity.Current?.SetTag(HttpClientActivityTagNames.Method, request.Method.Method);
        Activity.Current?.SetTag(HttpClientActivityTagNames.UrlFull, WithoutQueryOrFragment(request.RequestUri));
    }

    private static void BackfillResponseTags(object? payload)
    {
        if (payload?.GetType().GetProperty("Response")?.GetValue(payload) is HttpResponseMessage response)
        {
            Activity.Current?.SetTag(HttpClientActivityTagNames.StatusCode, (int)response.StatusCode);
        }
    }

    private static void BackfillErrorTag(object? payload)
    {
        var exceptionTypeName = payload?.GetType().GetProperty("Exception")?.GetValue(payload) is Exception exception
            ? exception.GetType().Name
            : "connection_error";

        Activity.Current?.SetTag(HttpClientActivityTagNames.ErrorType, exceptionTypeName);
    }

    private static string? WithoutQueryOrFragment(Uri? uri) =>
        uri is null ? null : $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}";

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "NetApiFlow: HttpClient tag backfill failed for {DiagnosticEvent}")]
        public static partial void BackfillFailed(ILogger logger, string diagnosticEvent, Exception exception);
    }
}
#endif
