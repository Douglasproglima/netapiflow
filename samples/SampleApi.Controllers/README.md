# SampleApi.Controllers

ASP.NET Core Controllers sample wired to NetApiFlow: tracing, the Dashboard UI, and the MCP
server.

## Running

Requires a PostgreSQL instance reachable at the connection string in `appsettings.json`.

```bash
dotnet run --project samples/SampleApi.Controllers
```

The API listens at `http://localhost:60803`, with the Dashboard UI at
`http://localhost:60803/flow` and the MCP server at `http://localhost:60803/flow/mcp`.

## Verifying the MCP server with a real client

The MCP server only responds in `Development` (the default profile for this sample), per the
same security rule as the Dashboard.

To connect Claude Code to the running sample, add this to `.mcp.json` at the repository root (not
committed, since it points at a local port):

```json
{
  "mcpServers": {
    "netapiflow-sample": {
      "type": "http",
      "url": "http://localhost:60803/flow/mcp"
    }
  }
}
```

With the sample running, open a new Claude Code session in this repository and ask it to list
the available tools. It should show `list_recent_traces`, `get_trace` and `get_component_stats`.
Call a few endpoints on the sample (`GET /api/orders`, `GET /api/orders/{id}`) to generate
traces, then ask Claude Code to list recent traces or inspect one by id: the tool responses
should reflect the real requests just made.
