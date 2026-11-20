namespace NetApiFlow.TestSupport;

public sealed record SecretLeakCase(int Id, string Message, string[] SecretFragments);

public static class SecretLeakCases
{
    public const int MaxOutputLength = 300;

    public static readonly IReadOnlyList<SecretLeakCase> All =
    [
        new(0, "password=PW_SECRET joe@example.com " + new string('x', 700), ["PW_SECRET", "joe@example.com"]),
        new(1, "Authorization: Bearer TOKEN_A", ["TOKEN_A"]),
        new(2, "access_token=TOKEN_B", ["TOKEN_B"]),
        new(3, "client_secret=TOKEN_C", ["TOKEN_C"]),
        new(4, "DB_PASSWORD=TOKEN_D", ["TOKEN_D"]),
        new(5, "api_key=TOKEN_E", ["TOKEN_E"]),
        new(6, "apikey=TOKEN_F", ["TOKEN_F"]),
        new(7, "{\"password\":\"TOKEN_G\"}", ["TOKEN_G"]),
        new(8, "Server=db;Password=TOKEN_H;", ["TOKEN_H"]),
        new(9, "Server=db;Pwd=TOKEN_I;", ["TOKEN_I"]),
        new(10, "Bearer TOKEN_J", ["TOKEN_J"]),
        new(11, "xpassword=TOKEN_K", ["TOKEN_K"]),
        new(12, new string('A', 296) + " password=TOKEN_L", ["TOKEN_L"]),
    ];

    public static IEnumerable<object[]> Ids => All.Select(leakCase => new object[] { leakCase.Id });

    public static SecretLeakCase ById(int id) => All.Single(leakCase => leakCase.Id == id);
}
