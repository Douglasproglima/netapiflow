using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("items")]
public sealed class ItemsController : ControllerBase
{
    [HttpGet("{id:int}")]
    public string Get(int id) => $"item-{id}";
}
