using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Flow = NetApiFlow.NetApiFlow;

namespace NpgsqlCommandInsideStepVerification.Controllers;

[ApiController]
[Route("api")]
public sealed class SmokeController(IServiceProvider services) : ControllerBase
{
    private static string ConnectionString => Environment.GetEnvironmentVariable("SMOKE_PG")!;

    [HttpGet("raw")]
    public async Task<IActionResult> Raw() => Ok(await RunCommandAsync());

    [HttpGet("step")]
    public async Task<IActionResult> InStep()
    {
        using var step = Flow.Step("SmokeController", "InStep");
        return Ok(await RunCommandAsync());
    }

    [HttpGet("ef")]
    public async Task<IActionResult> EntityFramework()
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SmokeDbContext>();
        return Ok((await context.Database.SqlQueryRaw<int>("SELECT 42 AS \"Value\"").ToListAsync()).Single());
    }

    private static async Task<object?> RunCommandAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT 42", connection);
        return await command.ExecuteScalarAsync();
    }
}
