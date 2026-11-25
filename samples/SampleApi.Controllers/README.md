# SampleApi.Controllers

ASP.NET Core Controllers sample wired to NetApiFlow: tracing, the Dashboard UI, the MCP server and
OTLP export. Each controller calls a service, the service calls a repository, and the repository
queries PostgreSQL through EF Core.

## Running

The sample needs a PostgreSQL database. This container matches the connection string in
`appsettings.json`:

```bash
docker run --name netapiflow-sample-db -e POSTGRES_DB=netapiflow_sample \
  -e POSTGRES_PASSWORD=postgres -p 5432:5432 -d postgres:16
```

The sample creates its table and three orders on startup. From the repository root:

```bash
dotnet run --project samples/SampleApi.Controllers -f net10.0
```

The project targets `net8.0` and `net10.0`, so `dotnet run` needs `-f` to pick one. Use
`-f net8.0` to run it on .NET 8.

The API listens at `http://localhost:60803`, with the Dashboard UI at
`http://localhost:60803/flow` and the MCP server at `http://localhost:60803/flow/mcp`.

When you are done, stop the sample with Ctrl+C and remove the database:

```bash
docker rm -f netapiflow-sample-db
```

## Scenarios

Orders 1 and 3 are `Pending`. Order 2 is `Shipped`.

| Request | What the trace shows |
|---|---|
| `GET /api/orders/1` | Success: Controller, Service, Repository and the SQL command. |
| `GET /api/orders/2/confirm` | Business error: the request returns 500 and the failing step is marked. Order 2 is not `Pending`. |
| `GET /api/orders/1/slow` | Slow step: the service waits 300 ms before querying, above the 250 ms `SlowThreshold` in `appsettings.json`. |
| `GET /api/orders/parallel?ids=1&ids=2` | Two repository calls started together with `Task.WhenAll`. |
| `GET /api/orders/sequential?ids=1&ids=2` | The N+1 pattern: one SQL query per id. |
| `GET /api/orders/1/shipping-status` | An outbound HTTP call to `https://api.github.com/zen`, so it needs internet access. |

Every response carries an `X-NetApiFlow-TraceId` header. Open
`http://localhost:60803/flow?trace=<that id>` to see the flow of that request:

```bash
curl -i http://localhost:60803/api/orders/2/confirm
```

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
Call a few endpoints on the sample (`GET /api/orders/1`, `GET /api/orders/2/confirm`) to generate
traces, then ask Claude Code to list recent traces or inspect one by id: the tool responses
should reflect the real requests just made.

To check the server without a client, list its tools over HTTP:

```bash
curl -s -X POST http://localhost:60803/flow/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
```

## Exporting traces to an OTLP collector

Set `OtlpExport:Enabled` to `true` in `appsettings.json` (or `OtlpExport__Enabled=true` as an
environment variable) to export traces to an OTLP collector. The exporter sends to
`http://localhost:4317` unless you configure another endpoint. The sample does not start a
collector itself, so run one separately.

Aspire Dashboard, as a standalone container:

```bash
docker run --rm -d --name netapiflow-aspire -p 18888:18888 -p 4317:18889 \
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

Then start the sample with the export on:

```bash
OtlpExport__Enabled=true dotnet run --project samples/SampleApi.Controllers -f net10.0
```

The Aspire Dashboard asks for a login token. Its first log lines carry the full login URL:

```bash
docker logs netapiflow-aspire
```

Open that URL (`http://localhost:18888/login?t=...`), go to Traces, and call the sample's
endpoints to watch them arrive.

The id Aspire shows for a trace is the W3C trace id. In the NetApiFlow dashboard it is the
"W3C trace" field in the trace details. The "Trace" field above it is NetApiFlow's own id for the
request and is not the same value.

Stop the collector with `docker stop netapiflow-aspire`.
