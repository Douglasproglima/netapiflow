using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using NetApiFlow;
using Flow = NetApiFlow.NetApiFlow;

[ApiController]
[Route("db/sqlserver")]
public sealed class SqlServerController(DatabaseSettings settings) : ControllerBase
{
    [HttpGet("several-commands")]
    public async Task<IActionResult> SeveralCommands()
    {
        var seen = new List<CurrentActivityEntry> { CurrentActivityEntry.Snapshot("before") };
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        foreach (var table in new[] { "users", "orders", "users" })
        {
            await using var command = new SqlCommand($"SELECT count(*) FROM dbo.{table}", connection);
            await command.ExecuteScalarAsync();
            seen.Add(CurrentActivityEntry.Snapshot(table));
        }

        using (Flow.Step("Service", "Work", ComponentType.Service))
        {
        }

        return Ok(seen);
    }

    [HttpGet("fails")]
    public async Task<IActionResult> Fails()
    {
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT count(*) FROM dbo.users", connection);
        await command.ExecuteScalarAsync();
        throw new InvalidOperationException("The command above ran, and then the request failed on purpose.");
    }
}
