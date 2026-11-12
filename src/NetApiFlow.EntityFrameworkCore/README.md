# NetApiFlow.EntityFrameworkCore

Tracking of EF Core database commands as NetApiFlow steps, via a `DbCommandInterceptor`.

Two steps: `AddNetApiFlowEntityFrameworkCore()` registers the interceptor in DI, then you attach
it to each `DbContext` with `options.AddInterceptors(sp.GetRequiredService<FlowDbCommandInterceptor>())`.

See the [project README](https://github.com/Douglasproglima/netapiflow#readme) for quick start
and marking forms.
