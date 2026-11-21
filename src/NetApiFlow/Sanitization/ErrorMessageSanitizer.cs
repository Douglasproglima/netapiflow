using System.Text.RegularExpressions;

namespace NetApiFlow.Sanitization;

internal static partial class ErrorMessageSanitizer
{
    private const int MaxLength = 300;
    private const int MaxInspectedLength = 16_384;
    private const string RedactedValue = "[redacted]";
    private const string RedactedJwt = "[redacted-jwt]";
    private const string EscapedQuote = "\\\"";

    private const string KeyCharacters = @"[\w.\[\]-]*";
    private const string KeyQuoteAndSeparator = @"(?<keyquote>\\?[""']?)(?<sep>\s*[:=]\s*)";
    private const string QuotedValue =
        @"(?<quoted>\\""(?:[^\\]|\\[^""])*(?:\\"")?|""(?:[^""\\]|\\.)*""?|'(?:[^'\\]|\\.)*'?)";
    private const string AuthorizationParameter = @"[\w.-]+=(?:""[^""]*""|[^\s,]*)";

    private const string AuthorizationPattern =
        "(?<key>" + KeyCharacters + "authorization" + KeyCharacters + ")" + KeyQuoteAndSeparator
        + "(?:" + QuotedValue + @"|(?<unquoted>\[redacted\]|[^\s;]+(?:[ \t]+(?:" + AuthorizationParameter
        + @"|[^\s;]+))?(?:,[ \t]*" + AuthorizationParameter + ")*))";

    private const string CookiePattern =
        "(?<key>" + KeyCharacters + "cookie" + KeyCharacters + @")(?<keyquote>\\?[""']?)(?<sep>\s*:\s*)(?:"
        + QuotedValue + @"|(?<unquoted>[^\r\n]+))";

    private const string SecretKeyPattern =
        "(?<key>" + KeyCharacters + "(?:password|passwd|pwd|token|secret|api[-_]?key|passphrase|private[-_]?key)"
        + KeyCharacters + "|" + KeyCharacters + @"(?:accountkey|sharedaccesskey|sharedaccesssignature))"
        + KeyQuoteAndSeparator + "(?:" + QuotedValue + @"|(?<unquoted>(?:bearer[ \t]+)?[^\s;]+))";

    public static string? Sanitize(string? message)
    {
        if (message is null)
        {
            return null;
        }

        var sanitized = LimitInput(message);
        sanitized = AuthorizationRegex().Replace(sanitized, RedactPair);
        sanitized = CookieRegex().Replace(sanitized, RedactPair);
        sanitized = SecretKeyValueRegex().Replace(sanitized, RedactPair);
        sanitized = SignatureQueryRegex().Replace(sanitized, "sig=" + RedactedValue);
        sanitized = BearerTokenRegex().Replace(sanitized, "Bearer " + RedactedValue);
        sanitized = JwtRegex().Replace(sanitized, RedactedJwt);
        sanitized = UrlUserInfoRegex().Replace(sanitized, "$1" + RedactedValue + "@");
        sanitized = CnpjRegex().Replace(sanitized, "[redacted-cnpj]");
        sanitized = CpfRegex().Replace(sanitized, "[redacted-cpf]");
        sanitized = LongNumberRegex().Replace(sanitized, "[redacted-number]");
        sanitized = EmailRegex().Replace(sanitized, "[redacted-email]");

        return TruncateWithoutSplittingASurrogatePair(sanitized);
    }

    private static string RedactPair(Match match)
    {
        var key = match.Groups["key"].Value;
        var keyQuote = match.Groups["keyquote"].Value;
        if (keyQuote.Length == 0)
        {
            return key + "=" + RedactedValue;
        }

        var quoted = match.Groups["quoted"];
        var (open, close) = quoted.Success ? QuotesAround(quoted.Value) : (string.Empty, string.Empty);
        return key + keyQuote + match.Groups["sep"].Value + open + RedactedValue + close;
    }

    private static (string Open, string Close) QuotesAround(string quotedValue)
    {
        var open = quotedValue.StartsWith(EscapedQuote, StringComparison.Ordinal) ? EscapedQuote : quotedValue[..1];
        var isClosed = quotedValue.Length > open.Length && quotedValue.EndsWith(open, StringComparison.Ordinal);
        return (open, isClosed ? open : string.Empty);
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

    [GeneratedRegex(AuthorizationPattern, RegexOptions.NonBacktracking | RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex(CookiePattern, RegexOptions.NonBacktracking | RegexOptions.IgnoreCase)]
    private static partial Regex CookieRegex();

    [GeneratedRegex(SecretKeyPattern, RegexOptions.NonBacktracking | RegexOptions.IgnoreCase)]
    private static partial Regex SecretKeyValueRegex();

    [GeneratedRegex(@"\bsig=[^\s&;]+", RegexOptions.NonBacktracking | RegexOptions.IgnoreCase)]
    private static partial Regex SignatureQueryRegex();

    [GeneratedRegex(@"\bBearer[ \t]+[^\s;]+", RegexOptions.NonBacktracking | RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"\beyJ[\w-]+\.[\w-]+\.[\w-]*", RegexOptions.NonBacktracking)]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"(://[^\s/:@]+:)[^\s/@]+@", RegexOptions.NonBacktracking)]
    private static partial Regex UrlUserInfoRegex();
}
