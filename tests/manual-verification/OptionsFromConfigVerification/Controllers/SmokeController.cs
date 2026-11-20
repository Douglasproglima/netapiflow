using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Flow = NetApiFlow.NetApiFlow;

namespace OptionsFromConfigVerification.Controllers;

[ApiController]
[Route("api/smoke")]
public sealed class SmokeController(ILoggerFactory loggerFactory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        using var step = Flow.Step("SmokeController", "Get");
        loggerFactory.CreateLogger("SmokeController").LogWarning("smoke warning");

        await using var connection = new SqlConnection(Environment.GetEnvironmentVariable("SMOKE_SQL"));
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT 42", connection);
        return Ok(await command.ExecuteScalarAsync());
    }
}
