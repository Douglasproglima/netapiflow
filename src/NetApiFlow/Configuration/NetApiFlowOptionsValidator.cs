using Microsoft.Extensions.Options;

namespace NetApiFlow.Configuration;

/// <summary>Validates <see cref="NetApiFlowOptions"/> before the host finishes starting.</summary>
internal sealed class NetApiFlowOptionsValidator : IValidateOptions<NetApiFlowOptions>
{
    public ValidateOptionsResult Validate(string? name, NetApiFlowOptions options)
    {
        if (options.Retention.MaxTraces <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"NetApiFlow: Retention.MaxTraces must be greater than zero (was {options.Retention.MaxTraces}).");
        }

        if (options.Retention.MaxAge <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"NetApiFlow: Retention.MaxAge must be greater than zero (was {options.Retention.MaxAge}).");
        }

        if (options.Limits.MaxSpansPerTrace <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"NetApiFlow: Limits.MaxSpansPerTrace must be greater than zero (was {options.Limits.MaxSpansPerTrace}).");
        }

        if (options.SlowThreshold <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"NetApiFlow: SlowThreshold must be greater than zero (was {options.SlowThreshold}).");
        }

        return ValidateOptionsResult.Success;
    }
}
