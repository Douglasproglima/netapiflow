# NetApiFlow.Testing

Assertions over the flow a request actually took, for integration tests. Turns "this endpoint
must not hit the database more than twice" into a test that fails when it does, instead of
something you notice by eye in the dashboard.

```csharp
using NetApiFlow;
using NetApiFlow.Testing;

var response = await client.GetAsync("/api/orders/42");

response.GetFlowTrace(factory.Services)
    .ShouldHaveNoErrors()
    .ShouldCallAtMost(ComponentType.Database, 3)
    .ShouldHaveCalled("OrderRepository", "FindAsync");
```

No assertion library is pulled in: a failure throws `FlowAssertionException`, which every test
framework already reports as a failed test.

Install it at the same version as `NetApiFlow`: this package asks for the core at exactly its own version.

See the [project README](https://github.com/Douglasproglima/netapiflow#readme) for quick start
and marking forms.
