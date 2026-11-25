# NetApiFlow.Mcp

A read-only MCP (Model Context Protocol) server that lets an AI agent query NetApiFlow traces
through standard MCP tools, with no custom integration code.

```csharp
using NetApiFlow.Mcp;
using NetApiFlow.Mcp.Endpoints;

builder.Services.AddNetApiFlowMcp();

var app = builder.Build();
app.MapNetApiFlowMcp(); // mounted at /flow/mcp by default
```

There are three tools: `list_recent_traces`, `get_trace` and `get_component_stats`. None of them writes
anything. They exist only on the NetApiFlow endpoint, `/flow/mcp`. If your app hosts its own MCP server
(`AddMcpServer()` and `MapMcp(...)`), it does not serve these tools on its own endpoint, and `/flow/mcp`
does not serve the tools of your app.

## Security

The MCP server follows the same rule as the dashboard. An AI agent that reads trace data is the same
exposure risk as the visual dashboard.

- **Outside `Development`**, every request that the endpoint serves answers `404`, unless you configure
  `.RequireAuthorization(...)` on the endpoint or register an `IFlowAccessFilter`. A method that the
  endpoint does not serve answers `405`, which tells that the route exists. By default, with no session,
  the endpoint serves only `POST`, so `GET`, `DELETE` and `PUT` answer `405`. If your app keeps MCP
  sessions, it serves `GET` and `DELETE` too, and only `PUT` answers `405`. When the dashboard is mounted
  at `/flow`, a `GET` on `/flow/mcp` is answered by the dashboard, and it is a `404` outside
  `Development`.
- **`Host` and `Origin`.** The endpoint refuses a request whose `Host` or `Origin` is not its own, with a
  `403`. In `Development` the `Host` must be `localhost`, `127.0.0.1`, `[::1]` or a name declared in
  `AllowedHosts`. An MCP client that is not a browser, such as the MCP Inspector or the official C#
  client, sends no `Origin` and is not affected.

Install it at the same version as `NetApiFlow`: this package asks for the core at exactly its own version.

See the [project README](https://github.com/Douglasproglima/netapiflow#readme) for quick start,
what to set behind a reverse proxy, and the security headers.
