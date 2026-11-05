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

- Impeccable (ADR-0014) adopted as a dev-only visual quality guard: installed skill and hook for
  Claude Code sessions, `DESIGN.md` now generated from `docs/brand/tokens.md`, and a warn-only CI
  job runs the detector against a live sample on every PR that touches
  `NetApiFlow.Dashboard/ClientApp`.

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
