# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `NetApiFlow`: `WithoutNetApiFlowTracking()` keeps an endpoint, a group, `MapControllers()` or
  `MapHealthChecks()` out of tracing, for a health check, a metrics route or anything that must never
  show up in history. It is read from the matched endpoint, so it holds under `UsePathBase`, and it
  wins over `ExcludePaths`, `IncludePaths` and `WithNetApiFlowTracking()` on the same endpoint. The
  dashboard and the MCP server mark their own routes with it.
- `NetApiFlow`: `TrackFlowByConvention()` tracks the application's own services, repositories and
  handlers with one line, called after the registrations. `*Service` becomes a `Service` step,
  `*Repository` a `Repository` step and `*Handler` a `UseCase` step, for the types of the entry
  assembly, or of the assemblies and namespaces you name with `IncludeAssembly` and
  `IncludeNamespacePrefix`. It never touches a type of the framework, of a third-party package or of
  NetApiFlow, and it leaves MediatR handlers to `NetApiFlow.MediatR`. When the host starts it logs a
  warning naming a registration made after the call and a registration made by factory, which
  cannot be decorated. It is optional, `AddNetApiFlow()` never turns it on.
- `NetApiFlow`: a Minimal API endpoint becomes an `Endpoint` step with no marking, so the graph of
  the default `dotnet new webapi` app is no longer empty. The `CaptureMinimalApiEndpoints` option
  (`NetApiFlowOptions`, or `NetApiFlow:CaptureMinimalApiEndpoints` in configuration, default `true`)
  turns it off.
- `NetApiFlow`: the `EmitTraceIdHeader` option (`NetApiFlowOptions`, or `NetApiFlow:EmitTraceIdHeader`
  in configuration) turns off the `X-NetApiFlow-TraceId` response header, for an app that does not
  want the trace id in its responses. It is on by default in Development and off elsewhere, see
  Changed below. Requests are still traced with it off, but `GetFlowTrace` from `NetApiFlow.Testing`
  reads that header and throws, and its message now names the option.
- `NetApiFlow.Storage.PostgreSql` and `NetApiFlow.Storage.SqlServer`: the `AutoCreateSchema` option
  (default `true`, so nothing changes unless you set it). With `false` the storage runs no `CREATE`,
  `ALTER` or `UPDATE` on your database. On first use it only checks that the tables and the
  `route_key` column exist, and when something is missing it logs an error that says what and where
  the script is, without failing the request. For an application whose database user cannot run DDL.
- `NetApiFlow.Dashboard`: an application with an authorization `FallbackPolicy` that mounts the
  dashboard with neither `AllowAnonymous()` nor `RequireAuthorization(...)` now logs one warning when
  it starts. Until now the dashboard answered 401 and nothing said why. The warning names the 401 and
  how to fix it.
- `NetApiFlow` and `NetApiFlow.Dashboard`: `GET /flow/api/blueprint` takes an optional `before`, an
  ISO 8601 date and time, normally the `startedAt` of the trace being drawn. It answers with the last
  successful execution that started strictly before it, so a trace is never its own blueprint and a
  later execution no longer changes the drawing of an earlier one. Without `before` it answers as it
  did, and a `before` that is not a date is a 400. `ITraceStore` gets
  `GetLastSuccessfulAsync(method, routeKey, before, cancellationToken)` as a member with a default
  implementation, so a store written for an earlier release keeps compiling and loading. The default
  asks for the latest successful trace and returns it only if it started before `before`, so it can
  return nothing where an exact store would find an earlier one. The in-memory, PostgreSQL and SQL
  Server stores implement it exactly, with `started_at < before` on the index they already have.

### Changed

- **`NetApiFlow`: each request gets its own trace id, and the id of a trace is no longer the W3C trace
  id of the request.** The id is random and has the same format as before, 32 hexadecimal
  characters. `X-NetApiFlow-TraceId`, `NetApiFlow.GetRecentTrace`, `NetApiFlow.Testing`, the REST API,
  the stream and the MCP tools use it. `NetApiFlow.CurrentTraceId` now returns it, and `null` when the
  request has no trace in progress, including a request that is not traced, where it used to return
  the W3C trace id. The W3C trace id is in the tag `w3c.trace_id` of the trace and in
  `Activity.Current.TraceId`, and the details of a trace in the dashboard show both with their
  labels. The `TraceStarted` event of the stream and the detail of a trace that is still running
  carry `w3c.trace_id` and the tenant from the start, which they did not carry before, so a consumer
  of the stream can match the first event with OpenTelemetry. Every NetApiFlow activity carries the
  tag `netapiflow.trace_id`, so a span in an OTLP
  backend leads to its trace, and the exported spans keep the W3C trace id. A trace saved before
  stays readable. If a test or a log line compared `CurrentTraceId` or the header with
  `Activity.Current.TraceId`, read the W3C trace id from the tag.
- `NetApiFlow`: a step started with an explicit `ActivityContext` as its parent, in a thread that does
  not carry the `ExecutionContext` of the request, is not recorded any more. It used to be, because
  it shared the W3C trace id of the request. The README lists it next to `ExecutionContext.SuppressFlow()`.
- `NetApiFlow`: the trace of a request that matches an endpoint now opens when routing picks the
  endpoint, with its route already known. The `TraceStarted` event, the live list and the REST list
  show `/orders/{id}` where they used to show `(unmatched)` until the request ended. The start time is
  still the start of the request, and a step created before routing is kept.
- **`NetApiFlow`: a request that matches no endpoint is now recorded only when it ends.** A static
  file, a 404, a branch with its own terminal middleware and a WebSocket handler used to be listed as
  running, as `(unmatched)`, from the start of the request, and the live list and the stream showed
  its steps as they happened, such as an EF Core or `HttpClient` call. Now none of it appears until
  the request ends: the trace, with its steps, is published then. It is still recorded and listed.
  What you lose is seeing it while it runs, in the live list, in the stream and in
  `GET /traces/{id}`. This is what lets a route that you excluded, with `.WithoutNetApiFlowTracking()`
  or with `ExcludePaths` under a `PathBase`, never show up at all.
- `NetApiFlow`: `DecorateFlowTracked` no longer decorates a type of NetApiFlow's own packages,
  whatever the predicate says. A predicate such as `type.Name.EndsWith("Service")` used to put a
  proxy on NetApiFlow's own hosted services, so their `StartAsync` and `StopAsync` went through it.
- **`NetApiFlow`: every Minimal API endpoint now gets one step per traced request by default.** A
  trace of an app that did not mark its endpoints goes from 0 steps to 1, so the span count, the
  architecture map and the limit of 300 steps per trace see one more step per request, and a step
  you open inside a handler now has the `Endpoint` step as its parent. The step runs from routing
  to the end of the request, so it includes the middleware in between, such as authentication,
  while `.WithNetApiFlowTracking()` measures only the filters and the handler. It is marked as
  failed when the endpoint throws, and also when the response is a 5xx with no exception. Set
  `CaptureMinimalApiEndpoints = false` to go back to marking each endpoint. An endpoint that is
  already marked still gets one step.
- **`NetApiFlow`: from 0.6.0 the `X-NetApiFlow-TraceId` response header is sent only in Development by
  default. To keep it in Production, set `EmitTraceIdHeader = true`**, in code or in
  `NetApiFlow:EmitTraceIdHeader`. In Production it is no longer sent and nothing warns you, so a
  client, a log pipeline or a test that read it there stops finding it. The default is off there so a
  response does not announce that the application runs NetApiFlow. `NetApiFlow.Testing` keeps working
  under `WebApplicationFactory`, which runs in Development. If your tests run in another environment,
  set the option in the test host. The dashboard's Runner reads this header too, so turn it on if you
  use the Runner against a Production host.
- `NetApiFlow`: the step that `.WithNetApiFlowTracking()` records for a Minimal API endpoint is named
  from the handler, no longer from the framework's display name. It used to read
  `HTTP: GET /hello./hello` in the dashboard. A handler that is a named method is shown as its type
  and method (`Handlers.StaticGet`). A lambda or a local function is shown as `Minimal API` with its
  endpoint name when it has `WithName(...)`, or with its verbs and route (`GET /hello`) when it does
  not. Traces already stored in PostgreSQL or SQL Server keep the old names, so the architecture map
  and the diff show two nodes for the same endpoint until the old history ages out. If you assert on
  the step name with `NetApiFlow.Testing` or read it through the MCP tools, update the name you expect.
- **`NetApiFlow`: `net8.0` stops shipping in the first version published on or after 10 May 2027.**
  It left Microsoft's support window on 10 November 2026, and the package still ships an asset for
  it until then. If you need it after that date, stay on the last version that has it. This closes
  the open question that the notes of `0.4.1` left about when to stop shipping it.
- The CI now runs the whole test suite on the .NET 11 runtime too, on Linux, against the build of
  `net10.0`. A `net11.0` app uses the `net10.0` asset, and an asset of its own for `net11.0` will
  exist only on the day the code uses an API that only .NET 11 has.

### Fixed

- `NetApiFlow`: a failure inside NetApiFlow itself no longer reaches your request. Before, an exception while
  recording a step was thrown from your own code, where the step started or ended, and an exception while
  the middleware opened or closed the trace broke the response. A step with a reserved tag set to a value
  of the wrong type was enough. Now the failure is logged as an error that says the request was not
  affected, at most once every 30 seconds, and the request goes on. Only the trace or the step can be
  missing.
- `NetApiFlow`: a null, empty or blank pattern in `IncludePaths` or `ExcludePaths` (in code or in the
  `NetApiFlow` configuration section) now stops the app at startup, with a message that names the option
  and the position of the pattern. Before, the app started, no request was ever traced, and the only sign
  was a warning in the log every 30 seconds.
- `NetApiFlow.Storage.PostgreSql` and `NetApiFlow.Storage.SqlServer`: a trace is never stored without its
  steps. The trace and its steps were written one after the other with no transaction, so a failure while
  writing the steps left the trace in history with a step count and no step to show, and a new attempt to
  store the same trace failed with a primary key violation. Both are now written in one transaction, and a
  failure leaves nothing behind. The cost of a save did not change in the measurement.
- `NetApiFlow`: requests that carry the same `traceparent`, which is what happens when a service calls
  your API more than once, lost traces and mixed their steps. In sequence, the in-memory store kept
  only the last trace, and PostgreSQL and SQL Server kept only the first and logged a primary key
  violation for the others. At the same time, the steps of both requests went into one trace and the
  second request got no `X-NetApiFlow-TraceId`. A client could also pick the id of a trace and
  overwrite a stored one in the in-memory store. Each request is now its own trace, and the client
  controls only the tag `w3c.trace_id`. This is a defect of correctness and not a vulnerability: a
  client can only collide with an id it chose itself, and it cannot read or delete the trace of
  someone else.
- `NetApiFlow.Dashboard`: a request that was still running was drawn without the steps it was
  expected to run, so the Waiting state never showed. The panel asked for the blueprint of a running
  request under the route `(unmatched)`, because the route was only known at the end. The route is
  known from the start now, so the expected steps show as Waiting while the request runs and as Not
  executed when it ends without them. A test in the CI now opens the panel in a real browser against
  a new app, holds a request, and checks both states and that an older trace is not redrawn by a
  later execution.
- `NetApiFlow`: under `app.UsePathBase("/api")`, `ExcludePaths`, `IncludePaths` and
  `OpenApiDocumentPath` compared the full path, `/api/health`, with patterns written for `/health`,
  so none of them matched. Now, whenever the request matches an endpoint, they value the path your
  application sees. The dashboard (pages, REST API and stream) and the MCP server were traced as if
  they were your traffic, and in Production, where they answer 404, they were traced too. A second
  dashboard client saw the stream of the first one as a trace that stayed `Running` for the whole
  connection. They are now never traced, with or without a `PathBase`. A request that matches no
  endpoint, a static file served by `UseStaticFiles` or a 404, is still decided by the full path, and
  the pattern has to carry the prefix. The README says so.
- `NetApiFlow`: `IncludePaths("/orders")` under `UsePathBase` left your own route out of the store,
  because the filter compared `/api/orders`. The route is kept now.
- `NetApiFlow`: a path pattern that cannot be read, such as a `null` in `IncludePaths`, no longer
  makes every request fail with a `NullReferenceException`. The request is answered and not traced,
  and a warning is logged at most once every 30 seconds.
- `NetApiFlow.Dashboard`: the graph of a trace no longer changes when a newer execution of the
  endpoint arrives. The steps drawn as "Not executed" came from the latest successful execution of
  the endpoint, even one that started after the trace you were looking at, and it could be the trace
  itself. They now come from the last successful execution that started before the trace. When there
  is none, the graph shows only what happened. A trace with no matched route asks for no blueprint.
- `NetApiFlow.EntityFrameworkCore`: a query cancelled by its `CancellationToken` no longer leaves its
  step open. The interceptor did not handle the cancellation event, so the step stayed in its
  internal table for good and the span kept showing as running. The span now closes as failed with
  an `OperationCanceledException`, and the step is released.
- `NetApiFlow`: `DecorateFlowTracked` no longer leaves the services it decorates undisposed. It built
  the original with `ActivatorUtilities`, so a decorated service that implements `IDisposable` was
  never disposed. The container now owns the original and disposes it with the lifetime the service
  was registered with. The original is registered with a private key, so the container has to
  support keyed services. The default container does, from .NET 8.
- `NetApiFlow`: a call through a tracked interface (`AddFlowTracked` or `DecorateFlowTracked`) that
  returns a cancelled `Task` or `ValueTask` now hands the caller a cancelled task. It used to hand
  back a faulted one, and the span closed as a success. The span now closes as failed with the
  cancellation exception.
- `NetApiFlow`: a store that fails to save no longer fills the log with one error and a stack trace
  per trace. A failed save logs a warning with the type and the message of the exception, and the
  next trace is tried normally. After three failures in a row the writer discards the traces that
  arrive for 30 seconds while counting them in `netapiflow.persistence.failures`, then tries again
  with one warning for each attempt that fails, and logs one line when saving works again. You see
  it with `AutoCreateSchema = false` before the tables exist, and when the database is down. The
  full exception is still logged at `Debug`.
- `NetApiFlow.Storage.PostgreSql` and `NetApiFlow.Storage.SqlServer`: with `AutoCreateSchema = false`,
  a schema check that failed on a transient error, such as a dropped connection, was marked as done
  for good and never ran again. It now runs again on the next call until it completes, and logs the
  failure at most once every 30 seconds.
- `NetApiFlow`: a request that throws an exception nothing in your pipeline handled now keeps the
  `X-NetApiFlow-TraceId` header in Development. The developer exception page rewrites the response
  before NetApiFlow could write the header, so the 500 came back without it. The dashboard's Runner
  then showed only "The request failed." and `GetFlowTrace` from `NetApiFlow.Testing` said NetApiFlow
  did not track the request when it had. The header is now written at the moment the response starts.
  One 500 still carries no header even with the option on: in Production with no exception handler,
  the server builds the 500 itself and drops the headers set before, so add `UseExceptionHandler` if
  you need it there. Measured on Kestrel, `net8.0` and `net10.0`. NetApiFlow does not change that
  response.
- `NetApiFlow.Storage.PostgreSql` and `NetApiFlow.Storage.SqlServer`: `schema.sql` is now the whole
  schema. It was missing the `route_key` column and its index, so a database created by hand from the
  script was rejected by the code. It now also carries the four indexes and the deploy annotations
  table the package creates, and an upgrade section that adds `route_key` and fills it for tables
  written by an earlier version. It is safe to run again.
- `NetApiFlow`: the request that fetches your OpenAPI document (`OpenApiDocumentPath`, `/openapi/v1.json`
  by default) is no longer traced. The dashboard fetches that document on its own, so it used to show
  up in recent executions and in the metrics as if it were your traffic. The match ignores case and
  only covers that one path.
- `NetApiFlow.Dashboard`: a trace with no steps no longer draws a blank graph. It now says the
  request was recorded but nothing was marked, and shows how to mark a step: MVC controllers need
  nothing, a Minimal API endpoint needs `.WithNetApiFlowTracking()`, and other code can call
  `NetApiFlow.Step(...)` or use `AddFlowTracked`. The message is in English and Portuguese.
- `NetApiFlow.Dashboard`: the details panel no longer says that 100% of the time is uncovered for a
  trace whose steps last microseconds, and the waterfall bars of such a trace no longer all start at
  the same point. Both are now measured in microseconds, the precision the timestamps already carry.
- `NetApiFlow.Dashboard`: a step name cut with an ellipsis now shows its full text in a tooltip, on the
  graph node and on the waterfall label of the details panel. Before, two routes of the same group
  looked alike once the end of the name was cut. A node whose operation fits shows no tooltip.

### Security

- `NetApiFlow.Dashboard` and `NetApiFlow.Mcp`: chaining `AllowAnonymous()` together with
  `RequireAuthorization(...)` no longer opens the dashboard and the MCP endpoint outside Development.
  The authorization middleware ignores the policy when an endpoint also allows anonymous access, and
  the access gate only checked that a policy was present. So every dashboard route was served,
  including `DELETE /flow/api/traces`, which deletes every stored trace, and the MCP endpoint
  answered. A route now counts as protected only when it requires authorization and does not allow
  anonymous access. If you relied on the two chained calls to open the dashboard, it now answers 404:
  register an `IFlowAccessFilter` instead.
- `NetApiFlow`: the error message of a failed step is now sanitized and truncated to 300 characters
  before it is written on the `Activity`, so it reaches OpenTelemetry (OTLP) clean. It used to be
  exported as thrown, with no sanitization and no length limit, while the dashboard showed the
  sanitized version.
- `NetApiFlow`: the error message sanitizer now sanitizes first and truncates after, so a secret cut
  by the 300 character limit leaves no piece behind, and it covers the forms it missed:
  `Authorization: Bearer ...` and `Basic ...` (the whole value, not only the scheme), `access_token=`,
  `client_secret=`, `DB_PASSWORD=`, `api_key=` and `apikey=`, `Pwd=` and `passwd=` pairs, quoted
  values, `"password":"..."` in JSON, and a bare `Bearer <token>`. The key is matched by containing
  a sensitive word, so a harmless `key: value` message such as `Unexpected token: '}'` now loses its
  value too.
- `NetApiFlow`: the error message sanitizer now also covers an `Authorization` value of any length
  (`Digest`, an AWS signature, with all its parameters), `token: Bearer ...`, a quoted value with an
  escaped quote inside, a key in brackets such as `config[password]`, `Cookie:` and `Set-Cookie:`
  headers, the Azure keys `AccountKey`, `SharedAccessKey` and `SharedAccessSignature`, `sig=` in a
  signed URL, the keys `passphrase` and `private_key`, a bare JWT, and the password in a URL
  (`scheme://user:password@host`). A secret in a quoted JSON string now leaves valid JSON,
  `{"password":"[redacted]"}`, where it used to leave `{"password=[redacted]}`. The sanitizer is a
  filter for known shapes and does not promise that no secret survives. The README now says so, and
  lists what it does not touch: `Basic <base64>` without `Authorization`, `credentials: ...`, a
  cookie in `name=value` form, an unquoted value with a space or a `;` (it is cut there, as in a
  connection string), a JSON value that is not a string, and a URL password with an unencoded `@`.
- `NetApiFlow.Dashboard` and `NetApiFlow.Mcp`: both now refuse a request whose `Host` or `Origin` is
  not their own, with a 403 that says what to change. They used to answer any page on the web, so
  with the dashboard or the MCP endpoint open, which is the default in Development, a page under an
  attacker's control could use DNS rebinding to read your traces, delete all of them with
  `DELETE /flow/api/traces` and call the MCP tools. In Development the `Host` must now be
  `localhost`, `127.0.0.1`, `[::1]` or a name you declare in `AllowedHosts` (a `*` does not count),
  so opening the dashboard by a LAN address, `host.docker.internal` or a WSL address needs that
  declaration. In every environment an `Origin` must match the `Host` the application sees, and the
  scheme is not compared. Behind a reverse proxy that rewrites the `Host`, keep the original `Host`
  or call `UseForwardedHeaders` with `XForwardedHost`, otherwise a `DELETE` from the browser gets 403.
- `NetApiFlow.Dashboard` and `NetApiFlow.Mcp`: every response now carries `X-Content-Type-Options:
  nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY` and `Content-Security-Policy:
  frame-ancestors 'none'`, the 403 and 404 of the access check included. Before, nothing was sent, so
  any page could show the dashboard in a frame and place its delete button under a click. The
  dashboard can no longer be embedded in a frame of another page. The dashboard page also carries a
  full policy with `script-src 'self'` and no `'unsafe-inline'`. Its `style-src` allows inline styles,
  because Angular adds the styles of each component at run time. To keep `script-src` strict the
  dashboard build no longer inlines its critical CSS, so the page loads one stylesheet file instead,
  and `index.html` went from about 14 KB to under 1 KB. If your middleware sets a
  `Content-Security-Policy` on the dashboard or MCP routes, the one of the package replaces it.
- `NetApiFlow.Dashboard`: `POST /flow/api/deploys` now reads and validates its body after the access
  check. Before, a body that failed to bind (`{}`, broken JSON, or a content type that is not JSON)
  was answered with 400 or 415 before the check ran, so a caller without access learned that the
  route exists and what it expects, instead of the 404 or 403 every other route gives, and the
  README said every dashboard route answers 404 by default. A caller without access now gets the
  404 or the 403 whatever the body is. With access, a bad body is still a 400 and a wrong content
  type a 415. The other routes only take optional query values, and a malformed one was already
  answered by the check first.

## [0.5.3] - 2026-11-20

### Fixed

- `NetApiFlow`: a failure of the trace store no longer stops your application. When the store threw
  while the retention service applied the retention policy, for example with PostgreSQL unreachable,
  the exception left the background service on its next five-minute tick, and the host's default for
  a failed background service (`StopHost`) ended the process. The service now logs a warning and
  tries again at the next interval. The writer that saves traces had the same flaw with a
  cancellation the store threw on its own outside a shutdown, such as a command timeout: it now
  counts a failed save and goes on with the next trace.

## [0.5.2] - 2026-11-20

### Fixed

- `NetApiFlow.Dashboard`: the routes the dashboard maps itself (`/flow/api/*`, its static route and
  the live stream) no longer show up in the app's OpenAPI document. An app that published the
  document listed 14 dashboard paths, which in Production answer 404 unless the dashboard is
  authorized, so an SDK generated from it got endpoints that do not exist.
- `NetApiFlow.Mcp`: the MCP endpoint is now marked as excluded from the document too. It was not
  listed with the MCP SDK this package ships against, and the mark keeps it out if a newer SDK
  exposes it to the API explorer.

## [0.5.1] - 2026-11-20

### Fixed

- `NetApiFlow.EntityFrameworkCore`: the package asked for EF Core 9 even on `net8.0`, so an app on
  EF Core 8 with an EF 8 provider failed with a `TypeLoadException` as soon as the package was
  installed. It now asks for EF Core 8.0.10, the first version of the 8 line without a known
  vulnerability, and works with EF Core 8, 9 and 10.

## [0.5.0] - 2026-11-19

### Added

- `NetApiFlow.Dashboard`: the dashboard now reaches the same depth on a narrow screen as it does
  on desktop, across all six screens (Flow, History, Components, Runner, Architecture, Insights).
  Nothing that used to require a wide window is hidden or unreachable on a phone anymore: the
  History trace ID and its copy action, the graph legend, the metric cards, and the endpoint
  runner's form all stay fully usable. The graph itself gains a proper height floor and automatic
  top-to-bottom orientation on a tall, narrow canvas, with the same automatic orientation now also
  applying to the Architecture screen's graph. The same depth now holds on a short desktop window:
  the graph legend shrinks to icons on a low canvas, the `/components` table scrolls inside its own
  card, the metric cards show at most three digits of duration, and the Runner form fits in a
  single scroll.
- `NetApiFlow.Testing`: new package, published for the first time in this release. It adds
  assertions over the flow a request actually took, for integration tests, such as how many
  database calls an endpoint makes. It pulls in no assertion library.
- `NetApiFlow`: SQL Server commands issued outside EF Core (Dapper, a plain `SqlCommand`) become
  database spans through `ExternalActivitySource.SqlClient`. The application's own
  `Microsoft.Data.SqlClient` has to be `6.0` or newer, and installing
  `NetApiFlow.Storage.SqlServer` does not raise it (its floor stays `5.2.2`). An app on an older
  driver has to update its own SqlClient to see these spans.
- `NetApiFlow.Dashboard`: the Runner shows the time left on a pasted JWT in the Authorize panel,
  marks the routes that require authentication, and retries the OpenAPI document with the
  remembered bearer token when the first request returns 401. The Runner URL is now resolved
  against the host's `PathBase` through `GET /config`, which closes the limitation documented in
  0.3.2.
- `NetApiFlow`, `NetApiFlow.Storage.PostgreSql`, `NetApiFlow.Storage.SqlServer`: a route recorded
  with a routing constraint (`/api/orders/{id:int}`) and the same route listed from an OpenAPI
  document without one (`/api/orders/{id}`) are now recognized as the same route by the
  architecture map and the blueprint lookup. Both already matched correctly once a parameter's
  presence or absence lined up; the mismatch only showed up for a constrained parameter.

### Changed

- `NetApiFlow`: the native external sources (`HttpClient`, `Npgsql` and `SqlClient`) are on by
  default in the Development environment, so an upgraded app shows new spans without any
  configuration change. Set `ExternalSources = ExternalActivitySource.None` to turn them off. A
  value stated explicitly, in code or in configuration, still wins over the default.
- `ITraceStore.GetArchitectureMapAsync` and `GetLastSuccessfulAsync` take `routeKey`, not
  `routeTemplate`: the value is compared ignoring a route parameter's constraint, not by exact
  string equality anymore. The two official storage packages already do this. A custom
  `ITraceStore` implementation needs the same comparison rule for these two methods specifically;
  every other method keeps comparing the recorded route template as-is.

### Fixed

- `NetApiFlow.Storage.SqlServer`: the HTTP method is compared without regard to letter case,
  whatever the database collation.
- `NetApiFlow.Dashboard`: the in-flight request counter updates again after a failed live traces
  call.
- `NetApiFlow.Dashboard`: a warning appears when none of the OpenAPI document's authorization
  alternatives applies.
- `NetApiFlow.Dashboard`: the Runner route list keeps three rows visible in a short window.
- `NetApiFlow`: with `ExternalActivitySource.Npgsql` on, a SQL command issued inside an action, a
  service, a repository or any other NetApiFlow step (Dapper, a plain `NpgsqlCommand`) produced no
  span. Only commands issued outside every step were recorded, which in an MVC action means none.
  They are now recorded as children of the step that issued them, and EF Core commands are still
  recorded once.
- `NetApiFlow`: `LogCorrelation.Enabled` and `CapturePrePipelineSteps` set only in the `NetApiFlow`
  section of the configuration changed the option's value but did not turn the feature on. They
  now do, the same as when set in the lambda passed to `AddNetApiFlow`. Left off, both cost
  nothing per request.
- All packages: the nuget.org page and the Visual Studio package manager showed the SDK's
  placeholder description ("Package Description") and no tags. Every package now has a one-line
  description, tags and a project URL, and `dotnet pack` fails when one is missing.

## [0.4.4] - 2026-11-18

### Fixed

- `NetApiFlow.Dashboard`: on a short browser window, such as the content height a real 1080p
  monitor leaves after its taskbar and the browser's own tabs/address bar, the replay bar's play
  button could be clipped out of the graph card and become unclickable. The replay bar no longer
  shares the graph card's clipped area, so it always keeps its own space and stays fully visible
  and clickable regardless of viewport height.

## [0.4.3] - 2026-11-18

### Fixed

- `NetApiFlow`: `AddNetApiFlow()` on its own reported six Native AOT and trimming warnings
  (`IL2026`/`IL3050`), from the reflection-based `ConfigurationBinder` calls that read
  `NetApiFlowOptions` from the `NetApiFlow` configuration section. Every Native AOT host got them,
  whether or not it used a configuration file. The core now builds with
  `EnableConfigurationBindingGenerator`, so that binding is generated at compile time: a host
  calling only `AddNetApiFlow()` and `NetApiFlow.Step(...)` publishes with zero warnings. The
  values read, and the configuration keys they come from, are unchanged.
- `NetApiFlow`: a disposed `FlowAuthorizationFilterListener` could still open a step, and a
  disposed `HttpClientActivityTagBackfill` could still write a tag. Disposing a
  `DiagnosticListener` subscription does not synchronously stop delivery, so an event already in
  flight reached the observer after `Dispose()` returned. A step opened that late was never closed
  by anything. Both now ignore events once disposed.

### Documentation

- README.md/README.pt-BR.md and the `[RequiresDynamicCode]` messages on
  `AddFlowTracked`/`DecorateFlowTracked` no longer present the `NetApiFlow.EntityFrameworkCore`
  interceptor as a Native AOT safe alternative. `NetApiFlow.Step(...)` is, and is now proven by a
  check in the repository. Any path through EF Core inherits EF Core's own documented Native AOT
  limitation, which NetApiFlow cannot remove.

## [0.4.2] - 2026-11-17

### Fixed

- `NetApiFlow.MediatR`: a host resolving `MediatR` 12.5.0 or newer threw
  `System.MissingMethodException` inside `FlowTrackingBehavior.Handle` on the first request through
  the pipeline. `MediatR` 12.5.0 added an optional `CancellationToken` parameter to
  `RequestHandlerDelegate<TResponse>`, which compiles against either version but changes the method
  the compiled assembly looks for at run time. The package now builds against `12.5.0` and passes
  the request's `CancellationToken` to the next delegate explicitly, so the token also reaches the
  rest of the pipeline instead of being dropped.

`MediatR` stays on `12.5.0`, the last Apache-2.0 release. From `13.0.0` it is dual-licensed under
RPL-1.5 plus a commercial license, which does not fit the MIT-compatible rule this project applies
to every dependency.

## [0.4.1] - 2026-11-16

### Fixed

- `NetApiFlow`: `FlowStep.MarkError` no longer writes to the caught exception's own `Data`
  dictionary to track which step first claimed its origin. A non-string key there could surprise
  application or logging code that enumerates `Data`; the tracking now lives in a
  `ConditionalWeakTable` outside the exception itself.
- `NetApiFlow.Dashboard`: a `net8.0` host no longer risks `Microsoft.Extensions.FileProviders.Embedded`
  resolving to a `10.x` assembly it did not ask for. The dependency is now pinned per target
  framework (`8.0.34` for `net8.0`, `10.0.0` for `net10.0`), with package pruning explicitly
  disabled for the `net8.0` group so the package's build-time manifest generation still runs.
- `AddFlowTracked`/`DecorateFlowTracked` are now annotated `[RequiresDynamicCode]` (and
  `DecorateFlowTracked` also `[RequiresUnreferencedCode]`), so a Native AOT build reports the
  incompatibility at build time instead of failing at startup with no warning.
- README.md/README.pt-BR.md: the manual marking and trace-assertion snippets
  (`NetApiFlow.Step(...)`, `NetApiFlow.GetRecentTrace(...)`) did not compile when copied into code
  with no namespace declared, for example a minimal API's top-level `Program.cs`, because of the
  name collision between the `NetApiFlow` namespace and the `NetApiFlow` class. Both snippets now
  add `using static NetApiFlow.NetApiFlow;` and call the member unqualified.

### Known limitation

- `GetRecentTrace(IServiceProvider, string)`'s XML doc now states explicitly that it is meant for
  integration tests built on `WebApplicationFactory`, not for use inside the normal request
  pipeline.
- The live trace view (`GET /traces/live`, the "Live" tab) reads the in-memory recorder of
  whichever replica answered the call; behind a load balancer without sticky sessions, each
  request can land on a different replica. Documented in the README; persisted history (with a
  storage package configured) does not have this problem.
- `net8.0` left Microsoft's official support window on 10 November 2026. NetApiFlow still ships an
  asset for it for now, because the next LTS (`net12.0`) is not expected until November 2027; when
  to stop shipping `net8.0` is still an open question.

## [0.4.0] - 2026-11-16

### Added

- `NetApiFlow.Dashboard`: "Try an endpoint" reads the host's OpenAPI `securitySchemes` and shows
  an "Authorize" section above the request form. Paste a value for each declared scheme (Bearer,
  API key, or a pasted access token for OAuth2/OpenID Connect); the right header or query
  parameter is applied automatically to every request whose route requires it, a manually added
  header with the same name still wins, and the value lives only in `sessionStorage` for the
  current browser tab.

## [0.3.2] - 2026-11-15

### Added

- `NetApiFlow.Dashboard`: "Try an endpoint" now has an optional custom headers section (key/value
  pairs), for testing an endpoint that requires `Authorization` or any other header.

### Fixed

- `NetApiFlow.Dashboard`: `localStorage` access in the theme and language services no longer
  throws and blocks app startup in a private-browsing context, a storage-quota failure, or an
  iframe with storage blocked. Both services fall back to their default (system theme, pt-BR)
  instead.
- `NetApiFlow.Dashboard`: an unmatched URL (a typo, a stale bookmark, a removed route) now falls
  back to the flow page instead of leaving the router outlet blank.
- `NetApiFlow.Dashboard`: a transient network failure no longer leaves the metrics and recent-
  executions error state stuck for the rest of the session. The root cause ran deeper than the
  visible symptom: an unhandled error inside a `switchMap` chain silently killed the whole
  subscription, so auto-refresh stopped firing entirely after one failure, not just the message.
- `NetApiFlow.Dashboard`: a live graph node now responds to the Space key (same as Enter) and
  exposes `aria-pressed` for the selected node.
- `NetApiFlow.Dashboard`: the sidebar navigation is now wrapped in a `<nav>` landmark with
  `aria-current` marking the active route.
- `NetApiFlow.Dashboard`: the SSE connection chip now announces a state change via
  `aria-live="polite"`.
- `NetApiFlow.Dashboard`: replaying a trace at 4x speed no longer collapses the minimum visual
  delay between steps to an imperceptible value; a real multi-second gap between spans is now
  capped instead of freezing playback for its full duration.
- `NetApiFlow.Dashboard`: the live in-progress trace list now shows a translated status instead of
  the literal `"Running"` string regardless of the selected language.
- `NetApiFlow.Dashboard`: a translation whose text repeats the same placeholder more than once now
  has every occurrence replaced, not just the first.
- `NetApiFlow.Dashboard`: "Try an endpoint" now validates every path parameter has a value before
  sending the request, instead of silently turning an empty one into a broken URL segment.
- `NetApiFlow.Dashboard`: the embedded Angular build no longer exceeds its initial bundle budget
  (reduced from 805.71 kB to 528.23 kB via route-level lazy loading) and drops the deprecated
  `provideNoopAnimations()` provider.

### Known limitation

- `NetApiFlow.Dashboard`: "Try an endpoint" still targets the application's root path, not the
  dashboard's own mount path, so a host running behind a reverse proxy's `PathBase` sees requests
  resolved against the dashboard's `<base href>` (`PathBase` + the dashboard's own mount path)
  rather than `PathBase` alone. Fixing this precisely needs a new `GET /config` field separating
  the two, deferred to a future minor release.

## [0.3.1] - 2026-11-15

### Fixed

- `NetApiFlow`: a route template is now recorded and queried in one consistent shape (a leading
  slash, never a trailing one), regardless of whether the route was declared as a Controller
  attribute route or a Minimal API group. Previously, Controller-style routes were recorded
  without a leading slash, so the architecture map, `GET /blueprint`, and the `/traces`/
  `/architecture` route filters never matched their executions. Existing rows in
  `NetApiFlow.Storage.PostgreSql` and `NetApiFlow.Storage.SqlServer` are normalized in place the
  next time each store starts.
- `NetApiFlow.Dashboard`: "Try an endpoint" no longer ships a fixed "Scenarios" tab with sample
  routes that don't exist on the host's own API.
- `NetApiFlow.Dashboard`: metrics, the in-progress trace list, and the live graph no longer
  trigger a fresh HTTP request for every single SSE event during a burst of traces; requests are
  now coalesced.
- `NetApiFlow.Dashboard`: `/flow?trace=<id>` with an expired or invalid trace id now shows an
  explicit "trace not found" state instead of staying on "Loading" forever.
- `NetApiFlow.Dashboard`: replaying a trace no longer marks a step the replay hasn't reached yet
  as "not executed"; it now shows "waiting" until the replay actually reveals it.
- `NetApiFlow.Dashboard`: a live trace now uses `GET /blueprint` to mark a branch the blueprint
  predicted but the trace skipped as "not executed", instead of only working during replay.
- `NetApiFlow.Dashboard`: the live graph's legend no longer lists "Cancelled"/"Timeout", states
  that only apply to a whole trace, never to an individual node.
- `NetApiFlow.Dashboard`: a manual zoom or pan on the live graph is no longer undone by the next
  live update.
- `NetApiFlow.Dashboard`: an in-flight request superseded by a newer one (history, insights,
  architecture map, node/span selection) no longer lets its stale response overwrite the newer
  result.
- `NetApiFlow.Dashboard`: replay is disabled while a trace is still `Running`, and no longer
  resets its current step on every SSE event for the same trace.
- `NetApiFlow.Dashboard`: the exported architecture map SVG is now self-contained (resolved
  colors and embedded state icons), so it renders correctly outside the dashboard.
- `NetApiFlow.Dashboard`: the "Flow" sidebar item is only marked active on the root route, not on
  every route under it.
- `NetApiFlow.Dashboard`: `GET /config` is now fetched once and shared across every consumer,
  with a sane fallback and error handling when the call fails.
- `NetApiFlow.Dashboard`: the dead-route warning now adds a caveat when tail sampling is enabled,
  since sampling can explain the absence of traces as well as a genuinely unused route.
- `NetApiFlow.Dashboard`: the standalone `/runner` page no longer stretches its form edge to edge
  on wide viewports.
- `NetApiFlow.Dashboard`: the live graph's `viewBox` no longer grows without bound when the
  canvas is short (for example, while the replay bar is open), and caps how much of the canvas an
  overlay (the state legend) can reserve.
- `NetApiFlow.Dashboard`: the "Send request" button no longer shrinks below a readable height in
  a narrow flex layout.

## [0.3.0] - 2026-11-14

### Fixed

- `NetApiFlow`: a request that never matches a route (MVC Controllers or Minimal API) no longer
  persists the resolved request path. Every unmatched request now records the route template as
  `(unmatched)` instead, closing a gap where a path segment (an id, an email, a token sitting in
  the URL) could reach a trace before the route was known (ADR-0007). `(unmatched)` is recorded
  for any request without a matched route, not only 404s. A request that fails after matching a
  route (authorization, a thrown exception) still keeps the real route template.
- `NetApiFlow.EntityFrameworkCore`: attaching `FlowDbCommandInterceptor` more than once to the
  same `DbContext` type, for example once via `AddDbContext` and once via `AddDbContextFactory`,
  no longer leaves a phantom `Database` span stuck in `Running`. Only the attachment that reaches
  a command first opens its span; every other attachment reuses it instead of opening a second
  one.
- `NetApiFlow.Dashboard`: `/flow` no longer redirects to `/flow/flow`.
- `NetApiFlow.Dashboard`: the action panel's route list no longer grows past its column on wide
  screens (`>= 1600px` layout) when the OpenAPI document behind "Try an endpoint" lists a real
  number of routes. The list used to have no height limit of its own, so it pushed the flow graph
  out of view.

### Added

- `NetApiFlow`: `NetApiFlowOptions.OpenApiDocumentPath` (default `/openapi/v1.json`), so "Try an
  endpoint" can read an OpenAPI document served at a different path, such as the one Swashbuckle
  serves at `/swagger/v1/swagger.json`.
- `NetApiFlow`: `FlowStatus` gains `Cancelled` (the client disconnected before the request
  finished) and `Timeout` (an internal cancellation fired while the client was still connected),
  distinguishing the two instead of recording both as `Error` (ADR-0020). `Timeout` still counts
  toward `ErrorCount` and is always persisted under tail sampling, same as `Error`; `Cancelled`
  counts toward neither, since a client giving up is not a sign of a server-side problem.
  `NetApiFlow.Dashboard` shows the two with their own icon, color and filter option on `/history`.
- `NetApiFlow`: `IDeployAnnotationStore` and `DeployAnnotation`, a sibling of `ITraceStore` for
  marking a deploy on the `/history` timeline, shared across everyone who opens the dashboard.
  `NetApiFlow.Dashboard` exposes `POST`/`GET /flow/api/deploys`; the timestamp is always assigned
  by the server. `NetApiFlow.Storage.PostgreSql` and `NetApiFlow.Storage.SqlServer` persist
  annotations the same way they already persist traces.

## [0.2.0] - 2026-11-12

### Fixed

- `NetApiFlow`: tags attached with `FlowStep.Tag(key, value)` now reach `FlowSpan.Tags`. The
  public API existed since `0.1.0` and wrote to the underlying `Activity`, but nothing ever copied
  those values back into the span, so they never reached the REST API, the storage packages or
  `NetApiFlow.Mcp`. The copy happens when the span completes, so a tag added at any point in the
  step's life is picked up; every `netapiflow.`-prefixed name is excluded, since those already are
  first-class `FlowSpan` properties. Two new limits bound it, matching the rest of the product:
  `Limits.MaxTagsPerSpan` (default 20, extra tags discarded and counted by a new
  `netapiflow.tags.dropped` counter) and `Limits.MaxTagValueLength` (default 4000, longer values
  truncated, never dropped). Side effect of the same gap: `CaptureSqlText = true` with
  `NetApiFlow.EntityFrameworkCore` records the SQL text on the span again.
- `NetApiFlow.Dashboard`: semantic text (HTTP verb badges, status chips, error/warning banners,
  graph node state labels) sitting directly on its own tinted background now uses a dedicated
  `-on-tint` color token instead of the flat-surface base color, fixing six low-contrast pairs
  found by Impeccable (ADR-0014).
- `NetApiFlow.Dashboard`: interface text below the 11px floor (version label, graph sub-labels,
  input placeholders) raised to 11px or given a readable `::placeholder` color.
- `NetApiFlow.Dashboard`: card titles and body text now follow the dashboard's type scale (Inter
  14/20 for card titles, 13/20 for body/list items) instead of inheriting the page-title scale.
- `NetApiFlow.Dashboard`: the error box in the span details panel no longer reads as a card nested
  inside the details panel card.
- `NetApiFlow.Dashboard`: "Try an endpoint" moved from a button floating in the graph panel to the
  topbar, matching the `/flow` workspace guide.

### Added

- `NetApiFlow.Dashboard`: a "Scenarios" tab in the action panel, next to the existing "Custom"
  form, with four one-click requests (Success, Slow, Error, Sequential (N+1)) aimed at the bundled
  sample APIs, plus an artificial delay selector (none, 250 ms, 1000 ms, 3000 ms) applied to the
  scenario before it is dispatched. The delay travels as an `X-NetApiFlow-Demo-Delay-Ms` request
  header, honored by a `DemoDelayMiddleware` that lives in `samples/` only: it is never part of
  any published package, and the core never delays a request (D-106).
- `NetApiFlow.Dashboard`: live controls on the flow graph. Zoom and pan are now driven by visible
  buttons (zoom in, zoom out, fit to screen, plus the current percentage) instead of wheel and
  drag alone, a fixed legend lists the six node states with their icon and label, the metrics
  strip gained a p95 latency card with a sparkline over the last 50 executions, and a
  pause/resume toggle stops the graph from repainting while a trace is still running, replaying
  every buffered event in order when resumed.
- `NetApiFlow`: `NetApiFlow.GetRecentTrace(services, traceId)`, reading a trace that finished
  recently straight from the in-memory buffer the dashboard's `GET /traces/{traceId}` already
  uses, never falling back to `ITraceStore`. Built for an integration test that just awaited its
  own HTTP call: read `X-NetApiFlow-TraceId` off the response, call this, assert on `FlowSpan`s
  with plain LINQ and whatever assertion library the test project already uses. Returns `null`
  when the trace already aged out of the buffer or NetApiFlow was never registered.
- `NetApiFlow`: `NetApiFlowOptions.CapturePrePipelineSteps` (opt-in, default `false`), recording
  what runs before the controller or handler on MVC Controller routes: one `Service` span per
  Authorization filter (`AuthorizationFilters`, named after the filter type) and one around the
  Resource filter stage (`ResourceFilters`, named after the action), the latter being the parent
  of the `Controller` span already recorded. Disabled, nothing changes for anyone who did not
  turn it on. Minimal API routes have no MVC filter stage and are unaffected (ADR-0019).
- `NetApiFlow.Dashboard`: comparing two executions side by side. `/history` lets you select
  exactly two rows and open `/diff?a=<traceIdA>&b=<traceIdB>`, which matches the spans of both
  executions by `(Component, Operation)`, never by span id. A key repeated inside one execution
  (an N+1 loop) is summed into a single row instead of listed many times, and a key missing from
  one side always shows as "not executed" there, with its icon and label, never as a matched step
  with zero duration. Selecting two executions of different routes is a visible warning, not a
  block.
- `NetApiFlow.Dashboard`: exporting and importing a trace as JSON. "Download trace (.json)"
  serializes the trace already loaded on `/flow`, and "Import trace (.json)" reads a file back
  into the same graph, waterfall, metric cards and event feed. Both run entirely in the browser,
  with no HTTP call in either direction, so a trace recorded on one machine can be read on another
  that never saw the API or its database. An imported trace is marked with a permanent badge, is
  never persisted, and never reaches `/history`, `/insights`, `/metrics` or `/architecture`.
  Clicking one of its spans builds the ancestors and children locally, without median duration or
  delta: the receiving instance has no history for a trace it never recorded.
- `NetApiFlow`: `ExternalActivitySource.Npgsql`, ingesting the SQL command `Activity` the Npgsql
  driver already emits as a `Database` span (ADR-0018). Opt-in, `None` still the default. When
  `NetApiFlow.EntityFrameworkCore` is also in use, the driver's activity is dropped instead of
  producing a second span for the same command. Only the command type and the main table reach the
  span; the full SQL text still requires `CaptureSqlText`, and the connection string, database user
  and peer address the driver reports are never copied.
- `NetApiFlow`: log and exception correlation per step, via a second `ILoggerProvider` registered
  alongside the host's own providers (`NetApiFlowOptions.LogCorrelation`, ADR-0017). Disabled by
  default; enabled, every `ILogger` call at or above `MinLevel` (default `Warning`) made while a
  `FlowStep` is active is recorded on that span's new `Logs` field, sanitized by the same
  `ErrorMessageSanitizer` already used for `FlowStep.MarkError`. Bounded by
  `LimitsOptions.MaxLogEntriesPerSpan` (default 20), extra entries discarded and counted (new
  `netapiflow.logs.dropped` counter). `NetApiFlow.Dashboard`'s span details panel shows a new
  "Logs" section; `NetApiFlow.Mcp`'s `get_trace` returns the field with no extra wiring needed.
  `NetApiFlow.Storage.PostgreSql` and `NetApiFlow.Storage.SqlServer` persist the field in a new
  `logs` column on the spans table, same `jsonb`/`nvarchar(max)` treatment already used for `tags`.
- `NetApiFlow.Dashboard`: a new "Architecture map" page (`/architecture`, D-83), the static call
  graph of a single route built from every execution recorded within a chosen time window, not
  just the last one. `ITraceStore.GetArchitectureMapAsync` and `ArchitectureMap` (nodes by
  `(Component, Operation)`, edges by observed parent-child relationship), implemented by the
  in-memory store and both storage packages, exposed via `GET /architecture`. The occurrence
  percentage of a step below 100% is embedded in its label (e.g. `TryGetAsync (14%)`), computed by
  the frontend from `executionCount`/`totalExecutions`, same treatment as `/metrics`'s `errorRate`.
  Reuses the existing SVG graph renderer unmodified; exports the graph as Mermaid or SVG, both
  generated client-side from the data already loaded.
- `NetApiFlow`: tail-based sampling of persisted traces (`NetApiFlowOptions.TailSampling`, D-82).
  Disabled by default. Enabled, a trace with an error or a duration at or above `SlowThreshold`
  is always persisted, and any other trace is kept with probability `SuccessSampleRate` (default
  `0.1`). The decision is made in `PersistenceWriter`, before a trace reaches `ITraceStore`; the
  live view (`GET /traces/live`, SSE) is unaffected. `GET /config` exposes the current state so
  `/history` shows a banner when tail sampling is on. New `netapiflow.traces.sampled_out` counter.
- `NetApiFlow.OpenTelemetry`: a new optional package exporting NetApiFlow traces to any
  OTLP-compatible backend (Jaeger, Tempo, Aspire Dashboard, Honeycomb, a collector), via
  `AddNetApiFlowOtlpExport()`. Points the OpenTelemetry SDK at the existing `"NetApiFlow"`
  `ActivitySource` (ADR-0002), no custom serialization. `IncludeHttpClientSource` (default
  `false`) optionally observes the native `"System.Net.Http"` source, which is not sanitized by
  NetApiFlow's own URL sanitizer (ADR-0016, R-16). Published alongside the other seven packages
  on the same tag from this release onward.
- `NetApiFlow`: `ITraceStore.GetRouteStatsAsync` and `RouteStat`, aggregated statistics per HTTP
  method/route template (D-81), implemented by the in-memory store and both storage packages.
- `NetApiFlow.Dashboard`: `GET /metrics/routes` and a new "Insights" page (`/insights`), an
  aggregated view of every traceable route: slowest routes, top errors, and dead routes (routes
  the host API exposes but that never ran in the chosen time window).
- `NetApiFlow.Mcp`: a new optional package embedding a read-only MCP (Model Context Protocol)
  server over the existing `ITraceStore`, so an AI agent can list recent traces, get a trace by
  id, and read per-component statistics without any REST client code. Same Development-only gate
  as the dashboard (`IFlowAccessFilter`, ADR-0015); no write tool exists (ADR-0013). Published
  alongside the other six packages on the same tag from this release onward.
- `NetApiFlow`: outbound `HttpClient` calls can now be ingested as `Http` spans, opt-in via
  `NetApiFlowOptions.ExternalSources = ExternalActivitySource.HttpClient` (ADR-0012). The outbound
  URL is always reduced to `scheme://host[:port]/path`; query string and fragment are never
  recorded.
- `NetApiFlow.Dashboard`: the flow graph flags a suspected N+1 query pattern, a badge on every
  `Database` step whose component and operation repeat three or more times anywhere in the trace
  (not just under the same parent, since a classic N+1 loop puts each query under a different
  instance of the looped step). The span details panel shows the group's size and a shortcut to
  the first occurrence.
- Impeccable (ADR-0014) adopted as a dev-only visual quality guard: installed skill and hook for
  Claude Code sessions, `DESIGN.md` now generated from `docs/brand/tokens.md`, and a warn-only CI
  job runs the detector against a live sample on every PR that touches
  `NetApiFlow.Dashboard/ClientApp`.
- `NetApiFlow.Dashboard`: a finished trace now shows how much of its total duration isn't covered
  by any recorded step, as the union of every step's `[startedAt, endedAt)` interval rather than a
  naive sum of durations, so a nested step or two parallel `Task.WhenAll` branches are never
  counted twice (D-78).

## [0.1.1] - 2026-11-03

### Fixed

- `NetApiFlow.Dashboard`'s embedded Angular bundle rebuilt against a patched `@angular/build`,
  resolving a high-severity `vite`/`launch-editor` advisory (arbitrary command execution via a
  crafted editor CLI flag) that shipped in the `0.1.0` build tooling. No application behavior
  changed; the fix only updates the Angular framework packages compiled into the dashboard UI.

## [0.1.0] - 2026-10-22

### Added

- Core instrumentation (`NetApiFlow`): middleware that opens one trace per request, `NetApiFlow.Step(...)`
  for manually marked steps, route include/exclude glob matching, in-memory trace store with
  retention, bounded queues and discard counters so the instrumentation never blocks or crashes the
  host application.
- Marking forms beyond manual steps: `AddFlowTracked`/`DecorateFlowTracked` (proxy-based automatic
  tracking), `NetApiFlow.MediatR` (pipeline behavior) and `NetApiFlow.EntityFrameworkCore`
  (`DbCommand` interceptor).
- `NetApiFlow.Dashboard`: eleven-route REST API plus a live SSE stream (`GET /flow/api/stream`),
  the Angular 22 dashboard UI (live flow graph, replay, history, per-component metrics, an
  OpenAPI-driven request runner), embedded in the NuGet package itself — installing it is enough,
  no separate frontend build or deploy step.
- Dashboard access control: every route 404s outside `IHostEnvironment.IsDevelopment()` unless
  `RequireAuthorization` or a custom `IDashboardAuthorizationFilter` is configured.
- Persistent storage packages, `NetApiFlow.Storage.PostgreSql` and `NetApiFlow.Storage.SqlServer`,
  both implementing `ITraceStore` over raw ADO.NET with automatic schema creation and a
  `schema.sql` for manual migrations.
- Sample applications (`samples/SampleApi.Controllers`, `samples/SampleApi.MinimalMediatR`)
  demonstrating the controller-based and minimal-API/MediatR integration paths, with the dashboard
  mounted.
- `tests/NetApiFlow.Benchmarks`: BenchmarkDotNet suite measuring instrumentation overhead with
  tracing enabled, disabled and absent.
- `README.md`, `README.pt-BR.md` and `docs/architecture.md`.
- Dashboard UI language selector (pt-BR by default, English secondary), switching live without a
  reload.
- Manual light/dark theme toggle, alongside the automatic `prefers-color-scheme` detection that
  already existed.
- Column sorting in the trace history list (status, method, route, started at, duration), applied
  server-side across all three `ITraceStore` implementations so the order holds across pages.

### Fixed

- Metric cards and the recent-executions list on `/flow` now update live as new traces complete,
  instead of only on the page's initial load.
- The package version shown in the dashboard sidebar and the "About" dialog now always comes from
  `GET /config`, matching the installed package; it previously came from a constant baked in at
  Angular build time, which could drift out of sync with a rebuild outside the full MSBuild
  pipeline.
