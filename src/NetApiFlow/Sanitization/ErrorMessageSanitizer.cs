using System.Text.RegularExpressions;

namespace NetApiFlow.Sanitization;

/// <summary>
/// Truncates and sanitizes error messages before they enter the domain model (risco R-07).
/// Every pattern uses <see cref="RegexOptions.NonBacktracking"/>, which runs in linear time regardless
/// of input, so no pattern here is vulnerable to catastrophic backtracking.
/// </summary>
internal static partial class ErrorMessageSanitizer
{
    private const int MaxLength = 300;

    public static string? Sanitize(string? message)
    {
        if (message is null)
        {
            return null;
        }

        var truncated = message.Length > MaxLength ? message[..MaxLength] : message;

        var sanitized = CnpjRegex().Replace(truncated, "[redacted-cnpj]");
        sanitized = CpfRegex().Replace(sanitized, "[redacted-cpf]");
        sanitized = LongNumberRegex().Replace(sanitized, "[redacted-number]");
        sanitized = EmailRegex().Replace(sanitized, "[redacted-email]");
        sanitized = SecretKeyValueRegex().Replace(sanitized, "$1=[redacted]");

        return sanitized;
    }

    [GeneratedRegex(@"\b\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}\b", RegexOptions.NonBacktracking)]
    private static partial Regex CnpjRegex();

    [GeneratedRegex(@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b", RegexOptions.NonBacktracking)]
    private static partial Regex CpfRegex();

    [GeneratedRegex(@"\b\d{12,}\b", RegexOptions.NonBacktracking)]
    private static partial Regex LongNumberRegex();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.NonBacktracking)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b(password|token|authorization|api-key|secret)\s*[:=]\s*\S+", RegexOptions.NonBacktracking | RegexOptions.IgnoreCase)]
    private static partial Regex SecretKeyValueRegex();
}
