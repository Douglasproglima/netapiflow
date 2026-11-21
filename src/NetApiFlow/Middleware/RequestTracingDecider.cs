using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetApiFlow.Routing;
using NetApiFlow.Storage;

namespace NetApiFlow.Middleware;

internal sealed partial class RequestTracingDecider(IOptions<NetApiFlowOptions> options, ILogger<RequestTracingDecider> logger)
{
    private readonly IntervalLogLimiter _failureLog = new(TimeSpan.FromSeconds(30));
    private TracingRoutePolicy? _policy;

    private TracingRoutePolicy Policy => _policy ??= new TracingRoutePolicy(options.Value);

    public bool ShouldTraceFullPath(string path)
    {
        try
        {
            return Policy.ShouldTrace(path);
        }
        catch (Exception exception)
        {
            LogFailure(exception);
            return false;
        }
    }

    public bool ShouldTraceMatchedEndpoint(Endpoint? endpoint, string pathRelativeToPathBase)
    {
        try
        {
            return endpoint?.Metadata.GetMetadata<EndpointTracingExclusion>() is null
                && Policy.ShouldTrace(pathRelativeToPathBase);
        }
        catch (Exception exception)
        {
            LogFailure(exception);
            return false;
        }
    }

    public void LogFailure(Exception exception)
    {
        if (_failureLog.TryAcquire())
        {
            Log.DecisionFailed(logger, exception);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "NetApiFlow: could not decide whether to trace a request, so it is not traced. This is logged at most once every 30 seconds.")]
        public static partial void DecisionFailed(ILogger logger, Exception exception);
    }
}
