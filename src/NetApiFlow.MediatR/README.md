# NetApiFlow.MediatR

Automatic tracking of MediatR requests as NetApiFlow steps, via a pipeline behavior.

Register the behavior next to MediatR's own registration. Without `AddNetApiFlowMediatR()`, no
MediatR step is recorded.

```csharp
using NetApiFlow.MediatR;

builder.Services.AddMediatR(config => config.RegisterServicesFromAssemblyContaining<Program>());
builder.Services.AddNetApiFlowMediatR();
```

This package asks for MediatR 12.5.0, which is under the Apache-2.0 license. MediatR 13.0.0 and later
changed the license: their package carries a `LICENSE.md` that says Reciprocal Public License 1.5, or a
commercial license of Lucky Penny Software for those who do not want to release the source of what they
build. Read it before your app moves to MediatR 13 or later.

Install it at the same version as `NetApiFlow`: this package asks for the core at exactly its own version.

See the [project README](https://github.com/Douglasproglima/netapiflow#readme) for quick start
and marking forms.
