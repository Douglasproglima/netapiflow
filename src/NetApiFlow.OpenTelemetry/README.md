# NetApiFlow.OpenTelemetry

Exports NetApiFlow traces to any OTLP-compatible backend (Jaeger, Tempo, Aspire Dashboard,
Honeycomb, or any OpenTelemetry Collector), by pointing the OpenTelemetry SDK at the
`ActivitySource` NetApiFlow already produces.

See the [project README](https://github.com/Douglasproglima/netapiflow#readme) for quick start
and OTLP export guidance.
