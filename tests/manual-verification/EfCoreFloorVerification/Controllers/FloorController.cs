using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EfCoreFloorVerification.Controllers;

[ApiController]
[Route("api")]
public sealed class FloorController(FloorDbContext context) : ControllerBase
{
    [HttpGet("query")]
    public async Task<IActionResult> Query() => Ok(await context.Widgets.CountAsync());

    [HttpPost("insert")]
    public async Task<IActionResult> Insert()
    {
        context.Widgets.Add(new Widget { Name = "floor" });
        await context.SaveChangesAsync();
        return Ok();
    }
}
