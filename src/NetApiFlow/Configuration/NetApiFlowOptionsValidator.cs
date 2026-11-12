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

        if (options.TailSampling.SuccessSampleRate is < 0.0 or > 1.0)
        {
            return ValidateOptionsResult.Fail(
                $"NetApiFlow: TailSampling.SuccessSampleRate must be between 0.0 and 1.0 (was {options.TailSampling.SuccessSampleRate}).");
        }

        if (options.Limits.MaxLogEntriesPerSpan <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"NetApiFlow: Limits.MaxLogEntriesPerSpan must be greater than zero (was {options.Limits.MaxLogEntriesPerSpan}).");
        }

        if (options.Limits.MaxTagsPerSpan <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"NetApiFlow: Limits.MaxTagsPerSpan must be greater than zero (was {options.Limits.MaxTagsPerSpan}).");
        }

        if (options.Limits.MaxTagValueLength <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"NetApiFlow: Limits.MaxTagValueLength must be greater than zero (was {options.Limits.MaxTagValueLength}).");
        }

        if (string.IsNullOrEmpty(options.OpenApiDocumentPath) || !options.OpenApiDocumentPath.StartsWith('/'))
        {
            return ValidateOptionsResult.Fail(
                $"NetApiFlow: OpenApiDocumentPath must be root-relative and start with '/' (was '{options.OpenApiDocumentPath}').");
        }

        return ValidateOptionsResult.Success;
    }
}
