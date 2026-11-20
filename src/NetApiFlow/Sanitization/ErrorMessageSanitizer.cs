using System.Text.RegularExpressions;

namespace NetApiFlow.Sanitization;

internal static partial class ErrorMessageSanitizer
{
    private const int MaxLength = 300;
    private const int MaxInspectedLength = 16_384;
    private const string RedactedValue = "[redacted]";

    public static string? Sanitize(string? message)
    {
        if (message is null)
        {
            return null;
        }

        var sanitized = LimitInput(message);
        sanitized = AuthorizationRegex().Replace(sanitized, "$1=" + RedactedValue);
        sanitized = SecretKeyValueRegex().Replace(sanitized, "$1=" + RedactedValue);
        sanitized = BearerTokenRegex().Replace(sanitized, "Bearer " + RedactedValue);
        sanitized = CnpjRegex().Replace(sanitized, "[redacted-cnpj]");
        sanitized = CpfRegex().Replace(sanitized, "[redacted-cpf]");
        sanitized = LongNumberRegex().Replace(sanitized, "[redacted-number]");
        sanitized = EmailRegex().Replace(sanitized, "[redacted-email]");

        return TruncateWithoutSplittingASurrogatePair(sanitized);
    }

    private static string LimitInput(string message)
    {
        if (message.Length <= MaxInspectedLength)
        {
            return message;
        }

        var lastSpace = message.LastIndexOf(' ', MaxInspectedLength - 1);
        return message[..(lastSpace > 0 ? lastSpace : MaxInspectedLength)];
    }

    private static string TruncateWithoutSplittingASurrogatePair(string message)
    {
        if (message.Length <= MaxLength)
        {
            return message;
        }

        var length = char.IsHighSurrogate(message[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
        return message[..length];
    }

    [GeneratedRegex(@"\b\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}\b", RegexOptions.NonBacktracking)]
    private static partial Regex CnpjRegex();

    [GeneratedRegex(@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b", RegexOptions.NonBacktracking)]
    private static partial Regex CpfRegex();

    [GeneratedRegex(@"\b\d{12,}\b", RegexOptions.NonBacktracking)]
    private static partial Regex LongNumberRegex();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.NonBacktracking)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(
        @"([\w.-]*authorization[\w.-]*)[""']?\s*[:=]\s*(?:\[redacted\]|""[^""]*""?|'[^']*'?|[^\s;]+(?:[ \t]+[^\s;]+)?)",
        RegexOptions.NonBacktracking | RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex(
        @"([\w.-]*(?:password|passwd|pwd|token|secret|api[-_]?key)[\w.-]*)[""']?\s*[:=]\s*(?:\[redacted\]|""[^""]*""?|'[^']*'?|[^\s;]+)",
        RegexOptions.NonBacktracking | RegexOptions.IgnoreCase)]
    private static partial Regex SecretKeyValueRegex();

    [GeneratedRegex(@"\bBearer[ \t]+[^\s;]+", RegexOptions.NonBacktracking | RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenRegex();
}
