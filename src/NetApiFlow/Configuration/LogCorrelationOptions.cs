using Microsoft.Extensions.Logging;

namespace NetApiFlow;

/// <summary>Log and exception correlation per step, via an internal <c>ILoggerProvider</c> (ADR-0017).
/// Default: disabled.</summary>
public sealed class LogCorrelationOptions
{
    /// <summary>Enables log correlation. Default: <see langword="false"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Minimum <see cref="LogLevel"/> captured. Default: <see cref="LogLevel.Warning"/>.</summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Warning;
}
