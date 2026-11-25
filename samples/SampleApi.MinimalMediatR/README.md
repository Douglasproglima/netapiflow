# SampleApi.MinimalMediatR

Minimal API sample wired to NetApiFlow with MediatR. Each endpoint sends a MediatR request, the
handler calls a service, and the service calls a repository that queries an in-memory SQLite
database through EF Core. It needs no external service, no database container and no internet.

## Running

From the repository root:

```bash
dotnet run --project samples/SampleApi.MinimalMediatR -f net10.0
```

The project targets `net8.0` and `net10.0`, so `dotnet run` needs `-f` to pick one. Use
`-f net8.0` to run it on .NET 8.

The API listens at `http://localhost:60801`, with the Dashboard UI at
`http://localhost:60801/flow`. Stop it with Ctrl+C.

## Scenarios

The sample creates three orders on startup. Orders 1 and 3 are `Pending`. Order 2 is `Shipped`.

| Request | What the trace shows |
|---|---|
| `GET /api/orders/1` | Success: Endpoint, UseCase (the MediatR handler), Service, Repository and the SQL command. |
| `GET /api/orders/2/confirm` | Business error: the request returns 500 and the failing steps are marked. Order 2 is not `Pending`. |
| `GET /api/orders/1/slow` | Slow step: the service waits 300 ms before querying, above the 250 ms `SlowThreshold` in `appsettings.json`. |
| `GET /api/orders/parallel?ids=1&ids=2` | Two repository calls started together. |
| `GET /api/orders/sequential?ids=1&ids=2` | The N+1 pattern: one SQL query per id. |

Every response carries an `X-NetApiFlow-TraceId` header. Open
`http://localhost:60801/flow?trace=<that id>` to see the flow of that request:

```bash
curl -i http://localhost:60801/api/orders/2/confirm
```

This sample has no MCP server, no OTLP export and no outbound HTTP call. See
[`SampleApi.Controllers`](../SampleApi.Controllers/README.md) for those.
