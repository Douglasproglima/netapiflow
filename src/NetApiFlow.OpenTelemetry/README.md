# NetApiFlow.OpenTelemetry

Exports NetApiFlow traces to any OTLP-compatible backend (Jaeger, Tempo, Aspire Dashboard,
Honeycomb, or any OpenTelemetry Collector), by pointing the OpenTelemetry SDK at the
`ActivitySource` NetApiFlow already produces.

```csharp
using NetApiFlow.OpenTelemetry;

builder.Services.AddNetApiFlowOtlpExport();
```

If your app already configures OpenTelemetry, use `AddNetApiFlowSource()` (same namespace) and not
`AddNetApiFlowOtlpExport()`. It adds only the `"NetApiFlow"` source to the tracing you already have, so
the steps go to the exporters you already set up. `AddNetApiFlowOtlpExport()` adds its exporter to the
tracer provider of your app, and every source your app traces then also goes to the NetApiFlow endpoint.

Three things to know:

- **No `SERVER` span.** NetApiFlow exports its own steps, not the request itself, so the first step of
  each trace has a parent that is not in the backend. To export it, add
  `OpenTelemetry.Instrumentation.AspNetCore` in the same version as the OpenTelemetry SDK of this
  package (1.20.1 in this release) and call `.AddAspNetCoreInstrumentation()`. On .NET 8, do not take
  version 1.21.0 of that package: it pulls `System.Diagnostics.DiagnosticSource` 11.0.0, and the app
  stops answering with a `MissingMethodException`.
- **The service name.** With nothing set, the backend shows `unknown_service:<process name>`. Set the
  `OTEL_SERVICE_NAME` environment variable.
- **What is exported follows what NetApiFlow records.** With `Enabled = false`, or for a path that
  `ExcludePaths` leaves out, nothing is exported. The sampler of your own OpenTelemetry setup does not
  apply to these spans.

Install it at the same version as `NetApiFlow`: this package asks for the core at exactly its own version.

See the [project README](https://github.com/Douglasproglima/netapiflow#readme) for quick start
and OTLP export guidance.
