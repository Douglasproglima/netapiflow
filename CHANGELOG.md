# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed

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
