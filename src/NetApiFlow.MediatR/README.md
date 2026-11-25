# NetApiFlow.MediatR

Automatic tracking of MediatR requests as NetApiFlow steps, via a pipeline behavior.

Register the behavior next to MediatR's own registration. Without `AddNetApiFlowMediatR()`, no
MediatR step is recorded.

```csharp
using NetApiFlow.MediatR;

builder.Services.AddMediatR(config => config.RegisterServicesFromAssemblyContaining<Program>());
builder.Services.AddNetApiFlowMediatR();
```

See the [project README](https://github.com/Douglasproglima/netapiflow#readme) for quick start
and marking forms.
