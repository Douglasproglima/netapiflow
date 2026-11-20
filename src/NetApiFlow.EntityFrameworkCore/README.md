# NetApiFlow.EntityFrameworkCore

Tracking of EF Core database commands as NetApiFlow steps, via a `DbCommandInterceptor`.

Two steps: `AddNetApiFlowEntityFrameworkCore()` registers the interceptor in DI, then you attach
it to each `DbContext` with `options.AddInterceptors(sp.GetRequiredService<FlowDbCommandInterceptor>())`.

Works with EF Core 8.0.10 or later, on `net8.0` and on `net10.0`. Tested on EF Core 8, 9 and 10.

See the [project README](https://github.com/Douglasproglima/netapiflow#readme) for quick start
and marking forms.
