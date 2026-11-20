# NetApiFlow.EntityFrameworkCore

Tracking of EF Core database commands as NetApiFlow steps, via a `DbCommandInterceptor`.

Two steps: `AddNetApiFlowEntityFrameworkCore()` registers the interceptor in DI, then you attach
it to each `DbContext` with `options.AddInterceptors(sp.GetRequiredService<FlowDbCommandInterceptor>())`.

Requires EF Core 9 or later, on `net8.0` as well. An app on EF Core 8 with an EF 8 provider fails with
a `TypeLoadException` when the package is installed.

See the [project README](https://github.com/Douglasproglima/netapiflow#readme) for quick start
and marking forms.
