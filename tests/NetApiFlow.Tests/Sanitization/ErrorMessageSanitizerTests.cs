using System.Diagnostics;
using NetApiFlow.Sanitization;
using NetApiFlow.TestSupport;
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

    [Theory]
    [InlineData("Authorization: Bearer abc.def.ghi", "abc.def.ghi|Bearer")]
    [InlineData("authorization=Basic dXNlcjpwYXNz", "dXNlcjpwYXNz|Basic")]
    [InlineData("access_token=TOK_9f3a", "TOK_9f3a")]
    [InlineData("client_secret=CSEC_7b21", "CSEC_7b21")]
    [InlineData("DB_PASSWORD=DBPW_41", "DBPW_41")]
    [InlineData("xpassword=XPW_52", "XPW_52")]
    [InlineData("api_key=KEY_1a2b", "KEY_1a2b")]
    [InlineData("api-key=KEY_3c4d", "KEY_3c4d")]
    [InlineData("apikey=KEY_5e6f", "KEY_5e6f")]
    [InlineData("PASSWORD=PW_UPPER Token=TK_MIXED SECRET=SC_UPPER AUTHORIZATION=AU_UPPER", "PW_UPPER|TK_MIXED|SC_UPPER|AU_UPPER")]
    [InlineData("password=\"two words\"", "words")]
    [InlineData("password = 'two words'", "words")]
    [InlineData("{\"password\":\"JSON_PW\"}", "JSON_PW")]
    [InlineData("{\"access_token\": \"JSON_TOK\"}", "JSON_TOK")]
    [InlineData("Server=db;Password=CONN_PW;Database=app", "CONN_PW")]
    [InlineData("Server=db;Pwd=CONN_PWD;Database=app", "CONN_PWD")]
    [InlineData("Request failed with Bearer eyJhbGciOi.payload.signature", "eyJhbGciOi.payload.signature")]
    [InlineData("userPassword=UPW_61", "UPW_61")]
    [InlineData("passwd=PD_72", "PD_72")]
    [InlineData("X-Api-Key: XAK_83", "XAK_83")]
    [InlineData("refresh_token=RT_94", "RT_94")]
    [InlineData("Authorization: Foo abc.unknown.scheme", "Foo|abc.unknown.scheme")]
    public void Sanitize_WithASecretInAKnownShape_NoFragmentOfItSurvives(string input, string forbiddenFragments)
    {
        var result = ErrorMessageSanitizer.Sanitize(input);

        result.ShouldNotBeNull();
        foreach (var fragment in forbiddenFragments.Split('|'))
        {
            result.ShouldNotContain(fragment);
        }
    }

    [Theory]
    [MemberData(nameof(SecretLeakCases.Ids), MemberType = typeof(SecretLeakCases))]
    public void Sanitize_WithEachCaseTheAuditMeasured_LeavesNoFragmentOfTheSecretAndNoMoreThan300Characters(int id)
    {
        var leakCase = SecretLeakCases.ById(id);

        var result = ErrorMessageSanitizer.Sanitize(leakCase.Message);

        result.ShouldNotBeNull();
        foreach (var fragment in leakCase.SecretFragments)
        {
            result.ShouldNotContain(fragment);
        }

        result.Length.ShouldBeLessThanOrEqualTo(SecretLeakCases.MaxOutputLength);
    }

    [Fact]
    public void Sanitize_WithASecretStartingJustBeforeTheLimit_NeverLeaksItNorExceeds300Characters()
    {
        var message = new string('a', 297) + " password=TRUNC_PW";

        var result = ErrorMessageSanitizer.Sanitize(message);

        result.ShouldNotBeNull();
        result.ShouldNotContain("TRUNC_PW");
        result.Length.ShouldBeLessThanOrEqualTo(300);
    }

    [Fact]
    public void Sanitize_WithACpfCutByTheLimit_DoesNotLeaveItsFirstDigits()
    {
        var message = new string('a', 288) + " CPF 123.456.789-09";

        var result = ErrorMessageSanitizer.Sanitize(message);

        result.ShouldNotBeNull();
        result.ShouldNotContain("123.456");
        result.Length.ShouldBeLessThanOrEqualTo(300);
    }

    [Fact]
    public void Sanitize_WithAMegabyteMessageWithoutAnySpace_ReturnsTheFirst300CharactersQuickly()
    {
        var message = new string('x', 1_048_576);

        var stopwatch = Stopwatch.StartNew();
        var result = ErrorMessageSanitizer.Sanitize(message);
        stopwatch.Stop();

        result.ShouldBe(new string('x', 300));
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(100);
    }

    [Fact]
    public void Sanitize_WithAKeyStartingBeforeTheInspectedLimitAndItsValueCrossingIt_DropsTheKeyWithTheCutAndLeaksNothing()
    {
        var message = "password=" + new string('S', 16_300) + " password=" + new string('T', 500);

        var result = ErrorMessageSanitizer.Sanitize(message);

        result.ShouldBe("password=[redacted]");
    }

    [Fact]
    public void Sanitize_AppliedToAnAlreadySanitizedMessage_ChangesNothing()
    {
        var message = "Login failed Authorization: Bearer abc.def.ghi password=PW access_token=TOK john.doe@example.com";

        var once = ErrorMessageSanitizer.Sanitize(message);
        var twice = ErrorMessageSanitizer.Sanitize(once);

        twice.ShouldBe(once);
    }

    [Theory]
    [InlineData("Missing secret manager configuration")]
    [InlineData("Unexpected token } in JSON at position 5")]
    [InlineData("Value cannot be null. (Parameter 'tokenizer')")]
    [InlineData("Failed to read secretKey from vault")]
    public void Sanitize_WithAHarmlessMessageThatIsNotShapedLikeAKeyAndValue_IsLeftAsItWas(string message)
    {
        ErrorMessageSanitizer.Sanitize(message).ShouldBe(message);
    }

    [Theory]
    [InlineData(
        "Unexpected token: '}' at position 5",
        "Unexpected token=[redacted] at position 5")]
    [InlineData(
        "A task was canceled. CancellationToken=CancellationToken { IsCancellationRequested = False }",
        "A task was canceled. CancellationToken=[redacted] { IsCancellationRequested = False }")]
    public void Sanitize_WithAHarmlessMessageShapedLikeAKeyAndValue_RedactsTheValueBecauseItCannotBeToldFromASecret(
        string message,
        string expected)
    {
        ErrorMessageSanitizer.Sanitize(message).ShouldBe(expected);
    }

    [Fact]
    public void Sanitize_WhenMessageIsNull_ReturnsNull()
    {
        ErrorMessageSanitizer.Sanitize(null).ShouldBeNull();
    }

    [Fact]
    public void Sanitize_WithALongMessageAndAnEmailBeyondTheLimit_NeverExceeds300CharactersNorLeaksTheEmail()
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
