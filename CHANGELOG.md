# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-10-20

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
