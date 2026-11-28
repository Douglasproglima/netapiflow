using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NetApiFlow;
using Flow = NetApiFlow.NetApiFlow;

[ApiController]
[Route("db/postgres")]
public sealed class PostgresController(NpgsqlDataSource dataSource) : ControllerBase
{
    [HttpGet("several-commands")]
    public async Task<IActionResult> SeveralCommands()
    {
        var seen = new List<CurrentActivityEntry> { CurrentActivityEntry.Snapshot("before") };
        foreach (var table in new[] { "users", "orders", "users" })
        {
            await using var command = dataSource.CreateCommand($"SELECT count(*) FROM {table}");
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
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM users");
        await command.ExecuteScalarAsync();
        throw new InvalidOperationException("The command above ran, and then the request failed on purpose.");
    }
}
