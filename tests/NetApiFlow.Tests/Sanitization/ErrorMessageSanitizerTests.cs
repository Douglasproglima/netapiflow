using System.Diagnostics;
using NetApiFlow.Sanitization;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Sanitization;

public class ErrorMessageSanitizerTests
{
    [Theory]
    [InlineData("Contact me at john.doe@example.com for details", "Contact me at [redacted-email] for details")]
    [InlineData("CPF 123.456.789-09 is invalid", "CPF [redacted-cpf] is invalid")]
    [InlineData("CNPJ 12.345.678/0001-95 already exists", "CNPJ [redacted-cnpj] already exists")]
    [InlineData("account number 123456789012 not found", "account number [redacted-number] not found")]
    [InlineData("failed with password=hunter2 set", "failed with password=[redacted] set")]
    [InlineData("failed with Authorization: abc123xyz", "failed with Authorization=[redacted]")]
    public void Sanitize_RemovesSensitiveData(string input, string expected)
    {
        ErrorMessageSanitizer.Sanitize(input).ShouldBe(expected);
    }

    [Fact]
    public void Sanitize_WhenMessageIsNull_ReturnsNull()
    {
        ErrorMessageSanitizer.Sanitize(null).ShouldBeNull();
    }

    [Fact]
    public void Sanitize_TruncatesTo300CharactersBeforeSanitizing()
    {
        var longMessage = new string('a', 400) + " john.doe@example.com";

        var result = ErrorMessageSanitizer.Sanitize(longMessage);

        result.ShouldNotBeNull();
        result.Length.ShouldBeLessThanOrEqualTo(300);
        result.ShouldNotContain("@example.com");
    }

    [Fact]
    public void Sanitize_WithPathologicalInput_CompletesWithinTimeBudget()
    {
        var pathological = new string('1', 50_000) + "@" + new string('a', 50_000);

        var stopwatch = Stopwatch.StartNew();
        ErrorMessageSanitizer.Sanitize(pathological);
        stopwatch.Stop();

        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(100);
    }
}
