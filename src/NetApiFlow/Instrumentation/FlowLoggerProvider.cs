using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetApiFlow.Recording;

namespace NetApiFlow.Instrumentation;

/// <summary>Registers <see cref="FlowLogger"/> alongside the host's own logging providers. Always
/// registered, and the logger it hands out records nothing unless the resolved
/// <see cref="LogCorrelationOptions.Enabled"/> is <see langword="true"/>.</summary>
internal sealed class FlowLoggerProvider : ILoggerProvider
{
    private readonly FlowLogger _logger;

    public FlowLoggerProvider(FlowRecorder recorder, IOptions<NetApiFlowOptions> options)
    {
        _logger = new FlowLogger(recorder, options);
    }

    public ILogger CreateLogger(string categoryName) => _logger;

    public void Dispose()
    {
    }
}
