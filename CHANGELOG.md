# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `NetApiFlow.Dashboard`: the dashboard now reaches the same depth on a narrow screen as it does
  on desktop, across all six screens (Flow, History, Components, Runner, Architecture, Insights).
  Nothing that used to require a wide window is hidden or unreachable on a phone anymore: the
  History trace ID and its copy action, the graph legend, the metric cards, and the endpoint
  runner's form all stay fully usable. The graph itself gains a proper height floor and automatic
  top-to-bottom orientation on a tall, narrow canvas, with the same automatic orientation now also
  applying to the Architecture screen's graph.
- `NetApiFlow`, `NetApiFlow.Storage.PostgreSql`, `NetApiFlow.Storage.SqlServer`: a route recorded
  with a routing constraint (`/api/orders/{id:int}`) and the same route listed from an OpenAPI
  document without one (`/api/orders/{id}`) are now recognized as the same route by the
  architecture map and the blueprint lookup. Both already matched correctly once a parameter's
  presence or absence lined up; the mismatch only showed up for a constrained parameter.

### Changed

- `ITraceStore.GetArchitectureMapAsync` and `GetLastSuccessfulAsync` take `routeKey`, not
  `routeTemplate`: the value is compared ignoring a route parameter's constraint, not by exact
  string equality anymore. The two official storage packages already do this. A custom
  `ITraceStore` implementation needs the same comparison rule for these two methods specifically;
  every other method keeps comparing the recorded route template as-is.

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
