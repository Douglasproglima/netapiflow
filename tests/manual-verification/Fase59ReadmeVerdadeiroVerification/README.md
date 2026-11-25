# Phase 59 verification: the README sentences, one by one

Each sentence of the audit's table was reproduced against a new app, with the packages packed from
`develop` (`7934db6`) as `0.6.0-verify.1`, never as a real version number. Run on 25 November 2026,
Windows 10, SDK 10.0.404, runtimes 8.0.34 and 10.0.15.

Three classes, the ones of Task 59.0:

- **False or imprecise**: the text changes.
- **True today**: an earlier phase already fixed it, or it was never wrong. The text stays.
- **Not reproduced**: none in this run.

## What is here

| File | What it is |
|---|---|
| `ProbeApp/` | One app for `net8.0` and `net10.0`. Every route is one scenario, and `Probe__*` environment variables switch the configuration. |
| `probe.sh` | Starts and stops the probe and the OTLP receiver. It checks that exactly one process listens on each port. |
| `otlp-receiver.py`, `summarize-spans.py` | A receiver of OTLP over HTTP that decodes the spans, and a summary of what it received. |
| `otlp-case.sh` | One OTLP scenario: restart the receiver and the app, send requests, print what the dashboard recorded and what OTLP received. |
| `show-trace.py`, `trace-of.py` | Read the steps of the last trace of a route, or of a trace by id. |
| `retention-timing.sh` | Counts the traces every 20 s while the retention runs. |
| `SqlProbe/`, `AotRun/`, `SwaggerNet8Program.cs.txt` | The small apps of rows 25, 9 and the `net8.0` quick start. |

## How to repeat it

From the repository root. The `-verify.<n>` suffix must change at every pack (D-153, D-174).

```bash
dotnet pack NetApiFlow.slnx -c Release -o tests/manual-verification/local-nuget-feed -p:Version=0.6.0-verify.1
dotnet build tests/manual-verification/Fase59ReadmeVerdadeiroVerification/ProbeApp -c Release
tests/manual-verification/Fase59ReadmeVerdadeiroVerification/otlp-case.sh baseline /hello,/target
```

The quick start rows use the script that the `real-package` job already has, on a new app:

```bash
dotnet new webapi -f net10.0 -n UserApp -o UserApp10
node scripts/build-user-app.mjs --app <abs path of UserApp10> --feed <abs path of local-nuget-feed> --version 0.6.0-verify.1
```

Run the same two commands with `-f net8.0` for the `net8.0` rows.

## Pitfalls of the measuring itself

They cost hours here, and each one gave a wrong number before it was found.

- Two receivers on port 4318 are possible on Windows, and the dump can come from the other one. `probe.sh`
  now fails unless exactly one process listens.
- `MSYS_NO_PATHCONV=1` breaks `taskkill //F` (write `/F`), `curl -o /dev/null` (write `-o NUL`) and
  POSIX paths given to `dotnet` and `python` (use `cygpath -m`). Without it, `route=/api/x` reaches the
  server as `route=C:/Program Files/Git/api/x`.
- A query to `Flow.GetRecentTrace` is itself a traced request and pushes the oldest trace out of the
  buffer. Exclude the route that does the reading.
- Chromium refuses ports 5060 and 5061 (`ERR_UNSAFE_PORT`).
- `OpenTelemetry.Instrumentation.AspNetCore` 1.21.0 breaks a `net8.0` app (row 19).

## The 26 rows

### 1. `SuppressFlow`: "record correctly, but as a new root". True today.

`GET /suppress` runs `Flow.Step` inside `Task.Run` under `ExecutionContext.SuppressFlow()`. The trace has
one span, `Endpoint Minimal API.GET /suppress`. `Suppressed.Inside` is in no trace. The README already
says that such a step starts a new root and is not attached to the trace of the request (Phase 55).

### 2. OTLP: "samples independently ... can still be exported". False.

`otlp-case.sh` with `AddNetApiFlowOtlpExport` and the receiver:

| Case | NetApiFlow dashboard | OTLP received |
|---|---|---|
| `GET /hello`, `GET /target` | 2 traces | 2 spans |
| `Enabled = false` | 0 | 0 |
| `ExcludePaths("/hello")` | `/target` only | `/target` only |
| host `AlwaysOffSampler` | 1 | 1 span (`Minimal API.GET /hello`) |

A disabled or excluded request exports nothing. The sampler of the host does not drop the NetApiFlow spans.

### 3. The warning of `IncludeHttpClientSource`. Imprecise, too pessimistic.

`GET /outbound` calls `http://127.0.0.1:5059/target?token=SECRET-QUERY-123#fragment`.
`net10.0`: CLIENT span `url.full = 'http://127.0.0.1:5059/target?*'`.
`net8.0`: CLIENT span `url.full = 'http://127.0.0.1:5059/target'`.
Neither carries the query or the fragment.

### 4. "Sensitive data": a body value in an exception message. False as written, it must say so.

`POST /body {"quantity":"my-secret-value-9876"}` with `int.Parse`. The recorded error message is
`The input string 'my-secret-value-9876' was not in a correct format.` The value of the body is in the trace.

### 5. `ExternalSources`, "default is None". The paragraph is true, the code comment is not.

`GET /outbound` with nothing configured: in `Development` the trace has `HttpClient.GET 127.0.0.1:5059/target`.
In `Production` it has only the `Endpoint` step. `Production` with `HttpClient` set has the span.
`Development` with `None` set has not. Only `HttpClient` was measured here. `Npgsql` and `SqlClient` have
their own integration tests. The comment `// opt-in; default is None` in the configuration block still
says `None` for every environment.

### 6. "Run an endpoint" and `scheme://host[:port]/path`. False twice.

The screen says "Try an endpoint" in English and "Testar um endpoint" in Portuguese. The recorded span is
`Http`, component `HttpClient`, operation `GET 127.0.0.1:5059/target`, no tags: no scheme, no query, no
fragment.

### 7. `CapturePrePipelineSteps`. Imprecise.

| Action | Steps |
|---|---|
| `[Authorize]` | `ResourceFilters.Authorized`, `PreController.Authorized`. No `AuthorizationFilters` step. |
| a filter that implements `IAuthorizationFilter` | `AuthorizationFilters.CustomAuthorizationFilterAttribute`, `ResourceFilters.Custom`, controller |
| `[OutputCache]`, first call | `ResourceFilters.Cached`, `CachedController.Cached`, 10.8 ms |
| `[OutputCache]`, cache hit | **no step**, 0.63 ms |
| Minimal API `.CacheOutput()`, cache hit | `Minimal API.GET /c` is still there, 2.07 ms |

Output caching is a middleware, not a resource filter. The cache hit was measured in a minimal app, because
the big probe did not cache for a reason that is not NetApiFlow: the same endpoints cache with NetApiFlow on,
off, with the dashboard and with MCP.

### 8. `IAsyncEnumerable`. Imprecise.

A tracked service returns `IAsyncEnumerable<int>` and throws while it is enumerated. `ItemFeed.StreamAsync`
is recorded in 0.19 ms as `Success`. Only the `Endpoint` step is `Error`.

### 9. Native AOT. The first sentence is imprecise, the second is true.

`AotRun/`: `CreateSlimBuilder`, `AddNetApiFlow`, `AddFlowTracked`, published for `win-x64` on `net10.0`
(11 840 512 bytes). The publish warns `IL3050` on the call of `AddFlowTracked` and `IL3053` on
`System.Reflection.DispatchProxy`. The binary starts. `GET /plain` is 200 and `GET /manual`, which uses
`Flow.Step`, is 200. `GET /tracked` is **500**: `PlatformNotSupportedException: Dynamic code generation is
not supported on this platform`, from `DispatchProxyGenerator`. It does not fail at startup: it fails on the
first call into a tracked service. The check that the README mentions is the pair of AOT publish steps in
`ci.yml`.

The publish needs `vswhere.exe` on the `PATH` (`C:\Program Files (x86)\Microsoft Visual Studio\Installer`).

### 10. "naming convention (e.g. `I*Repository`)". True today.

The predicate of `DecorateFlowTracked` received `ProbeApp.OrderRepository` and `ProbeApp.OrderService`, the
implementation types, in 150 calls (every registration with a known implementation, the framework's too).
The README already says that the predicate gets the concrete type and that an interface-only convention does
not work.

### 11. "only compiles from inside a namespace of your own". True today.

The sentence is in no README. The entry point is `Flow` since 0.6.0.

### 12. The "Asserting" blocks. False.

A new `dotnet new xunit` project that references a new app, with both blocks pasted as the README has them.

| TFM | Result |
|---|---|
| `net10.0` | `CS0246` for `WebApplicationFactory<>`. With `using Microsoft.AspNetCore.Mvc.Testing;` it builds with 0 warnings and 0 errors. |
| `net8.0` | with that `using`, `CS0122`: `Program` is inaccessible. With `public partial class Program { }` in the app it builds with 0 and 0. |

### 13. "in-memory buffer kept while persistence catches up". Imprecise.

600 requests, then `Flow.GetRecentTrace` for each trace id: it found 500, the indexes 100 to 599. It is a
fixed queue of the last 500 completed traces (`FlowRecorder.MaxRecentlyCompleted`). The dashboard store held
all 600, because its limit is `Retention.MaxTraces` (1000).

### 14. The p95 card, "Authorize ... above the request form", the preview. False three times.

- The "p95 latency" card and its sparkline are not visible at 1280, 1440 and 1599 px. They are at 1600 and 1920.
- Runner on `GET /orders/{id}` with an OpenAPI `bearer` scheme (the screen was in Portuguese): the parameter at
  y 186, **"Autorizar" at y 260**, "Adicionar header" at y 397. The section sits between the parameters and the
  headers.
- The field is a password input. After typing, the preview reads `Enviado como header: Authorization: Bearer ••••-123`.
  The value is masked, with the last four characters.

### 15. Import: "same ... metric cards and event feed". False.

After importing an exported trace (badge "Trace importado", action "Voltar aos traces ao vivo"), the cards still
read `13 Rastreamentos 12 Bem-sucedidas 1 Erros ... 335 ms Latência p95`, the numbers of the local instance, and
the feed reads "Aguardando eventos".

### 16. Dead routes. False.

`GET /flow/api/endpoints` lists 15 routes, `GET /openapi/{documentName}.json` among them, and the "Dead routes"
list of `/flow/insights` shows it too. The endpoint does not filter the OpenAPI document (roadmap item 26).

### 17. pt-BR gender. False.

`README.pt-BR.md` has "trace" 20 times in the feminine and 25 in the masculine. The screen says
"Trace importado", the masculine.

### 18. A manual step that throws and never calls `MarkError`. False as written, it must say so.

`using var step = Flow.Step("Manual", "Throws"); throw ...`: the step `Manual.Throws` is `Success` in
0.12 ms. The `Endpoint` step is `Error`.

### 19. OTLP leaves without the `SERVER` span and with an orphan root. False as written.

Every exported span has a parent id, and that parent is not exported: 0 `SERVER` spans. With
`OpenTelemetry.Instrumentation.AspNetCore` 1.20.1 and `.AddAspNetCoreInstrumentation()`, on `net8.0` and
`net10.0`, 0 warnings: the `SERVER` span `GET /hello` is exported and the NetApiFlow span is its child. The
requests of the dashboard are exported as `SERVER` spans too (`GET /flow/api/traces`). With
`options.Filter = context => !context.Request.Path.StartsWithSegments("/flow")` only `GET /hello` is exported.

**Trap on `net8.0`.** `OpenTelemetry.Instrumentation.AspNetCore` 1.21.0, the latest, pulls
`System.Diagnostics.DiagnosticSource` 11.0.0 and `Microsoft.Extensions.*` 11.0.0, and NuGet warns that they do
not support `net8.0`. The app then gets no response, with
`MissingMethodException: ... ActivityCreationOptions.set_TraceState`. Use the version of the SDK (1.20.1).

### 20. The tenant filter and `IFlowTenantResolver`. False, not documented.

0 mentions in the READMEs. In the code, `IFlowTenantResolver.Resolve(HttpContext)` puts the `tenant` tag on the
trace, and `GET /flow/api/traces?tenant=<id>` filters by it. With no resolver registered there is no tag.

### 21. `OTEL_SERVICE_NAME`, the retention, `schema.sql`. False three times.

- `OTEL_SERVICE_NAME`: 0 mentions. With nothing set, the exported `service.name` is `unknown_service:dotnet`
  for an app started with `dotnet app.dll` (and `unknown_service:SampleApi.Controllers` in the Aspire Dashboard
  for the sample started with `dotnet run`). With `OTEL_SERVICE_NAME=my-orders-api` it is `my-orders-api`.
- Retention, `retention-timing.sh` with `MaxAge = 15 s` and 5 traces at t=0: 5 traces until t=283 s and 0 at
  t=303 s. The first pass comes 5 minutes after the start (`RetentionInterval.Default` is 5 minutes and the
  timer is a `PeriodicTimer`).
- The READMEs of the two storage packages say "created by you from `schema.sql`" and do not say where it is.

### 22. Registration calls without the `using`. False for the storage packages.

The PostgreSQL block, pasted into the quick start, gives `CS1061` for `AddNetApiFlowPostgreSqlStorage`. With
`using NetApiFlow.Storage.PostgreSql;` it builds with 0 and 0. The README has two such blocks and a sentence
for SQL Server (`NetApiFlow.Storage.SqlServer`) with no namespace. The namespaces of MCP, OTLP, EF Core and
MediatR are already in the text.

### 23. The fixed limits. False, they are not documented.

The values, read from the code: 500 traces in progress, 500 recently completed, an event queue of 5000, a
persistence queue of 1000, 50 SSE clients, a replay buffer of 1000, a queue of 5000 per SSE client, an error
message cut at 300 characters, an outbound URL cut at 300.

### 24. `DELETE` and `POST /deploys`. False, they are not documented.

In `Development`: `POST /flow/api/deploys` is 201. `DELETE /flow/api/traces` is 204 and the traces went from
3 to 0: it deletes every trace. Both use the same access rule as the reads, and there is no read-only role.

### 25. `NetApiFlow.Storage.SqlServer` and MediatR. False, not documented.

`SqlProbe/` against a real SQL Server 2022 container, with `ExternalSources = SqlClient`, in an app that
references only `NetApiFlow.Storage.SqlServer`. The resolved driver is `Microsoft.Data.SqlClient` 5.2.2. `GET /sql`
runs `SELECT 1`, and the trace has only the `Endpoint` step. The startup log says, at `Information`, that the
driver reports its command events in a form NetApiFlow cannot read. With `Microsoft.Data.SqlClient` 6.1.8
declared in the app, the trace has `Database SqlClient.SELECT`.

`NetApiFlow.MediatR` asks for MediatR 12.5.0 (Apache-2.0). The `LICENSE.md` of the MediatR 13.0.0 package
(read from the package on nuget.org) says the code is under the Reciprocal Public License 1.5, or under a
commercial license of Lucky Penny Software for those who do not want to release the source of what they
build.

### 26. "every MCP route responds 404" outside `Development`. False.

A minimal app with `AddNetApiFlowMcp`, `MapNetApiFlowMcp` and the SDK `ModelContextProtocol.AspNetCore` 2.3.1,
with no authorization. The status of each verb on `/flow/mcp`, with `Accept: application/json, text/event-stream`:

| Dashboard | Environment | Session mode | GET | POST | DELETE | PUT |
|---|---|---|---|---|---|---|
| not mounted | `Production` | default | 405 | 404 | 405 | 405 |
| not mounted | `Production` | `Stateful` | 404 | 404 | 404 | 405 |
| not mounted | `Development` | default | 405 | 500 | 405 | 405 |
| mounted | `Production` | default | **404** | 404 | 405 | 405 |
| mounted | `Production` | `Stateless`, `StatefulForInitializeClients`, `Stateful` | 404 | 404 | 405 (`Stateless`) or 404 | 405 |
| mounted | `Development` | default | **200** | 500 | 405 | 405 |

Every request that the endpoint serves is 404, and a verb it does not serve is 405. By default the endpoint
serves only `POST`, and with sessions it serves `GET` and `DELETE` too. With the dashboard mounted, `GET
/flow/mcp` is not a request of the MCP endpoint at all: the dashboard answers it, with its own 404 in
`Production` and with the page of the dashboard (200) in `Development`.

My first run had the dashboard mounted and gave `GET` 404, and I took it for the rule of the MCP route. The
audit measured an app without the dashboard (`GET` 405 by default, 404 with sessions) and the two agree once
the dashboard is accounted for. The `POST` 500 in `Development` is a request with no body.

## The quick start on `net8.0` (D-277)

A new `dotnet new webapi -f net8.0` with the README blocks pasted by `scripts/build-user-app.mjs`: 2 errors,
`CS1061` for `AddOpenApi` and for `MapOpenApi`. The template of `net8.0` has `AddSwaggerGen`, `UseSwagger` and
`UseSwaggerUI`. The same app with the template lines kept and the NetApiFlow lines added
(`SwaggerNet8Program.cs.txt`) builds with 0 warnings and 0 errors.

The Runner of that app, with no `OpenApiDocumentPath`: "Could not load the OpenAPI document. Check that the host
serves it, and that `NetApiFlowOptions.OpenApiDocumentPath` points to where it is served." The list is empty.
With `OpenApiDocumentPath = "/swagger/v1/swagger.json"` the Runner reads the Swashbuckle document (OpenAPI 3.0.4)
and lists `GET /hello` and `GET /orders/{id}`. The quick start on `net10.0` builds with 0 warnings.
