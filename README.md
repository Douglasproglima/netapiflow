# NetApiFlow

See, live, the path every HTTP request takes through your own ASP.NET Core API: Controller/Endpoint → Handler → Services → Repositories → SQL, with per-step duration, where it failed, what was slow, and what never ran.

[![NuGet](https://img.shields.io/nuget/v/NetApiFlow.svg)](https://www.nuget.org/packages/NetApiFlow)
[![CI](https://github.com/Douglasproglima/netapiflow/actions/workflows/ci.yml/badge.svg)](https://github.com/Douglasproglima/netapiflow/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 8 | .NET 10](https://img.shields.io/badge/.NET-8%20%7C%2010-512BD4)](https://dotnet.microsoft.com/)

🇧🇷 [Leia em português](README.pt-BR.md)

![The flow graph lighting up live as a request executes](docs/images/flow-demo.gif)

> The GIF above is recorded from `samples/SampleApi.Controllers` with the dashboard mounted.
> See [Quick start](#quick-start) to reproduce it yourself.

[Website](https://douglasproglima.github.io/netapiflow-site/en/) · [Live demo](https://douglasproglima.github.io/netapiflow-site/demo/)

## What it is, and what it isn't

NetApiFlow instruments your own API's internal call path, not external latency, not
infrastructure metrics, not distributed tracing across services. It answers "where did *this*
request spend its time, inside *my* code" for a single ASP.NET Core process.

It is **not**:

- A distributed tracing system (no cross-service context propagation). NetApiFlow's own
  `ActivityListener` coexists peacefully with the OpenTelemetry SDK (see
  [`docs/adr/0002-activitysource-nativo-sem-otel-sdk.md`](docs/adr/0002-activitysource-nativo-sem-otel-sdk.md)),
  and `NetApiFlow.OpenTelemetry` exports its traces to any OTLP backend (see
  [OTLP export](#otlp-export)).
- An APM (no alerting, no dashboards beyond the one it ships, no long-term metrics storage
  beyond what you configure via a storage package).
- A profiler (it measures the steps you mark, not every method call in your process).

## Quick start

In a project created with `dotnet new webapi`:

```bash quickstart:packages
dotnet add package NetApiFlow
dotnet add package NetApiFlow.Dashboard
```

Then add the NetApiFlow lines to the `Program.cs` the template generated. `AddOpenApi()` and
`MapOpenApi()` are the template's own, so keep them: the dashboard's Runner lists your endpoints
from your app's OpenAPI document, which is `/openapi/v1.json` unless you set `OpenApiDocumentPath`.
The `/hello` line stands for your own endpoints.

```csharp quickstart:program
using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.Marking;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddNetApiFlow();
builder.Services.AddNetApiFlowDashboard();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapNetApiFlowDashboard(); // mounted at /flow by default

app.MapGet("/hello", () => "Hello");

app.Run();
```

Calling `AddNetApiFlow(...)`, `AddNetApiFlowDashboard()`, `AddNetApiFlowMediatR()` or
`AddNetApiFlowOtlpExport(...)` again is safe, for example from an extension method of your own or
from a test. Nothing is registered twice, and the options of every call to `AddNetApiFlow(...)` and
`AddNetApiFlowOtlpExport(...)` are applied, in the order of the calls.

MVC controllers and Minimal API endpoints become a step on their own, with nothing to mark. That
includes the default `dotnet new webapi` template. A Minimal API endpoint gets one `Endpoint` step
for each traced request, from the moment routing picks it to the end of the request, so the step
also counts the middleware that runs in between, such as authentication. A request that matches
no route, a 404 or a static file for example, is still recorded and listed, with zero steps.

To turn the automatic step off, set `CaptureMinimalApiEndpoints = false`. A Minimal API endpoint
then becomes a step only with `.WithNetApiFlowTracking()`, on the endpoint or on a whole group
(`app.MapGroup("/api").WithNetApiFlowTracking()`). Marking an endpoint while the option is on does
no harm: it never gets two steps.

To draw the layers under the endpoint too, add these lines where you register your own services,
before `builder.Build()`. The `using NetApiFlow.Marking;` at the top of the block above is for the
last one:

```csharp quickstart:registrations
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.TrackFlowByConvention();
```

Every interface of your own code whose implementation is named `*Service`, `*Repository` or
`*Handler` then becomes a step, with the matching component type. Call it after the
registrations: one made later is not tracked, and the host logs a warning that names it.
[Marking forms](#marking-forms) has the other ways.

Open `http://localhost:<port>/flow` and send a request to the endpoint. See
[Dashboard security in production](#dashboard-security-in-production) before deploying this
anywhere but your own machine.

## Marking forms

NetApiFlow needs to know where one step ends and the next begins. Pick whichever form fits each
layer. They compose freely in the same request. `AddFlowTracked`, `DecorateFlowTracked` and
`TrackFlowByConvention` are in the `NetApiFlow.Marking` namespace, so `Program.cs` needs
`using NetApiFlow.Marking;` at the top, next to the `using NetApiFlow;` the Quick start already has.
The `using NetApiFlow;` covers `.WithNetApiFlowTracking()` and `ComponentType`.

| Form | What it does | When to use it |
|---|---|---|
| `.WithNetApiFlowTracking()` | Wraps a Minimal API endpoint, or every endpoint of a `MapGroup`, in an `Endpoint` step | Only with `CaptureMinimalApiEndpoints = false`. Otherwise every Minimal API endpoint already gets the step, and MVC controllers always do |
| `Flow.Step(...)` | Manually opens and closes a step around a block of code | Any code, no dependency on DI |
| `.WithoutNetApiFlowTracking()` | Keeps an endpoint, a group, `MapControllers()` or `MapHealthChecks()` out of tracing | A health check, a metrics route, any route that must never show up in history, also under a `PathBase` |
| `AddFlowTracked<TInterface, TImpl>(...)` | Wraps every call to an interface's implementation in a step, via a dynamic proxy | A repository/service interface already registered in DI |
| `TrackFlowByConvention()` | Wraps the `*Service`, `*Repository` and `*Handler` interfaces of your own code in steps, each with its component type | A layered app: one line after your registrations |
| `DecorateFlowTracked(predicate, ...)` | Same as above, applied in bulk to every registration matching a predicate | Many interfaces that share a naming convention you choose yourself |
| `NetApiFlow.MediatR` | A pipeline behavior that wraps every `IRequest` handled by MediatR | Projects already using MediatR for commands/queries |
| `NetApiFlow.EntityFrameworkCore` | A `DbCommandInterceptor` that marks each SQL command as a `Database` step | Projects using EF Core |

Manual form, used explicitly:

```csharp
using NetApiFlow;

public async Task<Order?> FindAsync(int id, CancellationToken cancellationToken)
{
    using var step = Flow.Step(nameof(OrderRepository), nameof(FindAsync), ComponentType.Repository);
    return await _dbContext.Orders.FindAsync([id], cancellationToken);
}
```

`Flow` lives in the `NetApiFlow` namespace, so `using NetApiFlow;` is all it needs, from a
top-level `Program.cs` too. If another library you import also has a type named `Flow`, write
`NetApiFlow.Flow.Step(...)`, or give it an alias such as `using Tracing = NetApiFlow.Flow;` and call
`Tracing.Step(...)`.

Before 0.6.0 this class was `NetApiFlow.NetApiFlow`, and the dashboard and the MCP server were
mounted with `UseNetApiFlowDashboard` and `UseNetApiFlowMcp`. Those names still work and forward to the new
ones. They are marked `[Obsolete]`, so the compiler shows warning `CS0618` where you use them. They
will be removed in a later version, and the CHANGELOG will say so before that happens.

Automatic form, from `samples/SampleApi.Controllers/Program.cs`: `OrderService`/`OrderRepository`
carry no NetApiFlow code at all, every call to them is tracked because of this one registration:

```csharp
builder.Services.AddFlowTracked<IOrderRepository, OrderRepository>(ComponentType.Repository);
builder.Services.AddFlowTracked<IOrderService, OrderService>(ComponentType.Service);
```

One line covers the usual layers. Call `TrackFlowByConvention` after your own registrations:

```csharp
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.TrackFlowByConvention(); // after the registrations above
```

It tracks `*Service` as a `Service`, `*Repository` as a `Repository` and `*Handler` as a `UseCase`,
for the interfaces whose implementation is a type of your application's entry assembly. It never
touches a type of the framework, of a third-party package or of NetApiFlow. When the repositories
live in another assembly, name it, or a namespace:

```csharp
builder.Services.TrackFlowByConvention(options =>
{
    options.IncludeAssembly(typeof(OrderRepository).Assembly);
    options.IncludeNamespacePrefix("MyCompany.Orders");
});
```

Any of the two calls replaces the default scope, the entry assembly. Under `WebApplicationFactory`
the entry assembly is the test host, so name yours with
`options.IncludeAssembly(typeof(Program).Assembly)`, or the shortcut tracks none of your services
in those tests. A MediatR handler is left out,
because `NetApiFlow.MediatR` already opens its step. Two cases the shortcut cannot cover are
logged as warnings when the host starts, naming the types: a registration made after the call,
and a registration made by factory, for which `AddFlowTracked<TInterface, TImplementation>` is the
way. It stays optional, `AddNetApiFlow()` never turns it on.

`DecorateFlowTracked` is what it is built on: it decorates every interface already registered in DI
whose implementation type matches a predicate you write. Add the assembly to the predicate, or it
also matches types of the framework and of other packages:

```csharp
builder.Services.DecorateFlowTracked(
    type => type.Assembly == typeof(Program).Assembly && type.Name.EndsWith("Repository"),
    ComponentType.Repository);
```

The predicate receives the concrete implementation type registered in DI, never the interface, so
matching on an interface-only naming convention (`IOrderRepository`) will not work. A type of
NetApiFlow itself is never decorated, whatever the predicate says.

### EF Core database commands

`NetApiFlow.EntityFrameworkCore` takes two steps, not one. `AddNetApiFlowEntityFrameworkCore()`
only registers `FlowDbCommandInterceptor` in DI. You attach it to each `DbContext` yourself,
because EF Core has no global interceptor registration:

```csharp
builder.Services.AddNetApiFlowEntityFrameworkCore();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"));
    options.AddInterceptors(sp.GetRequiredService<FlowDbCommandInterceptor>());
});
```

`FlowDbCommandInterceptor` is in `NetApiFlow.EntityFrameworkCore`, so `Program.cs` also needs
`using NetApiFlow.EntityFrameworkCore;` at the top.

The package works with EF Core 8.0.10 or later, on `net8.0` and on `net10.0`. It was tested on
EF Core 8, 9 and 10.

### Tagging a step

A step opened with `Flow.Step(...)` accepts free key/value tags, recorded on that span:

```csharp
using NetApiFlow;

using var step = Flow.Step(nameof(OrderService), nameof(ConfirmAsync), ComponentType.Service);
step.Tag("order.status", order.Status);
step.Tag("payment.provider", provider.Name);
```

`Tag` takes a `string`, an `int`, a `long`, a `double`, a `bool` or an enum value. A number is
recorded as text in the invariant culture, a `bool` as `true` or `false`, and an enum value as the
name of its member. A null key or value is ignored and logs a warning, so a tag never breaks the
request.

Call `Tag` at any point while the step is open, not only right after opening it: tags are read off
the step when it completes. A key starting with `netapiflow.` is reserved and never recorded.
`Limits.MaxTagsPerSpan` caps how many tags a span keeps (extra ones are discarded and counted),
and `Limits.MaxTagValueLength` truncates a long value without dropping the tag. Both are in
[Configuration](#configuration).

> **A tag value is never sanitized.** Every other field NetApiFlow records is either structural or
> passed through the error sanitizer. A tag is free text you chose to store, so it is persisted
> and served exactly as written. Keep it to identifiers and short labels, never a request payload,
> a user's data or a credential.

Tags travel on `FlowSpan.Tags`: `GET /traces/{traceId}`, `Flow.GetRecentTrace`,
`NetApiFlow.Mcp`'s `get_trace` and both storage packages all carry them. The dashboard UI does not
display them in this version.

## Asserting on flow in tests

An integration test built on `WebApplicationFactory<TEntryPoint>` already has the response its
own request just produced. `Flow.GetRecentTrace` reads that trace straight from the
in-memory buffer kept while persistence catches up, so there's no `Task.Delay` or retry loop
waiting for the asynchronous write to finish:

```csharp
using NetApiFlow;

public class OrdersEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetOrder_DoesNotTriggerMoreThanOneDatabaseQuery()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders/42");

        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var trace = Flow.GetRecentTrace(factory.Services, traceId);

        Assert.NotNull(trace);
        Assert.Equal(1, trace.Spans.Count(s => s.Type == ComponentType.Database));
    }
}
```

`GetRecentTrace` returns a plain `FlowTraceDetail` (the `FlowTrace` and the `FlowSpan`s it
recorded), not an assertion DSL: query `trace.Spans` with whatever LINQ expresses the rule
("no more than N database calls", "this route never calls that repository", "no step failed")
and whatever assertion library the test project already uses, xUnit's own `Assert` included.
It returns `null` when the trace already aged out of the in-memory buffer (500 traces completed
between the call and the assertion) or when NetApiFlow was never registered, never when the HTTP
call itself failed.

### `NetApiFlow.Testing`

The same test, written with the optional `NetApiFlow.Testing` package, which skips the header
read and the null check and names the rule instead of spelling out the LINQ:

```csharp
using NetApiFlow;
using NetApiFlow.Testing;

public class OrdersEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetOrder_DoesNotTriggerMoreThanOneDatabaseQuery()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders/42");

        response.GetFlowTrace(factory.Services)
            .ShouldHaveNoErrors()
            .ShouldCallAtMost(ComponentType.Database, 1)
            .ShouldHaveCalled("OrderRepository", "FindAsync")
            .ShouldNeverCall("PaymentGateway");
    }
}
```

Five assertions, all chainable: `ShouldHaveStatus`, `ShouldHaveNoErrors`, `ShouldCallAtMost`,
`ShouldHaveCalled` and `ShouldNeverCall`. `ShouldHaveCalled`/`ShouldNeverCall` match on the
component alone when the operation is left out, or on the exact pair when it is given.

The package references nothing but `NetApiFlow` itself, so it imposes no assertion library on
your test project. A failed assertion throws `FlowAssertionException` with a message saying what
was expected and what was found, for example
`Expected at most 1 Database calls, found 5.`; every test framework reports that as a failure.
`GetFlowTrace` throws the same exception instead of returning `null` when the response carries no
`X-NetApiFlow-TraceId` header or the trace already left the buffer.

## Optional packages

| Package | Adds |
|---|---|
| [`NetApiFlow.Dashboard`](https://www.nuget.org/packages/NetApiFlow.Dashboard) | The REST/SSE API and the embedded Angular UI |
| [`NetApiFlow.MediatR`](https://www.nuget.org/packages/NetApiFlow.MediatR) | Automatic tracking of MediatR requests |
| [`NetApiFlow.EntityFrameworkCore`](https://www.nuget.org/packages/NetApiFlow.EntityFrameworkCore) | Tracking of EF Core database commands, once the interceptor is attached to your `DbContext` |
| [`NetApiFlow.Storage.PostgreSql`](https://www.nuget.org/packages/NetApiFlow.Storage.PostgreSql) | Persists traces to PostgreSQL instead of memory |
| [`NetApiFlow.Storage.SqlServer`](https://www.nuget.org/packages/NetApiFlow.Storage.SqlServer) | Persists traces to SQL Server instead of memory |
| [`NetApiFlow.Mcp`](https://www.nuget.org/packages/NetApiFlow.Mcp) | A read-only MCP server so an AI agent can query traces directly |
| [`NetApiFlow.OpenTelemetry`](https://www.nuget.org/packages/NetApiFlow.OpenTelemetry) | Exports traces to any OTLP backend (Jaeger, Tempo, Aspire Dashboard, Honeycomb, a collector) |
| [`NetApiFlow.Testing`](https://www.nuget.org/packages/NetApiFlow.Testing) | Assertions over the recorded flow, for integration tests |

The core (`NetApiFlow`) has zero dependencies beyond the ASP.NET Core shared framework. Every
one of the above is opt-in.

## Reading the graph

Every node is one step. Color reinforces its state, but never is the only signal: each state
also has a distinct icon and text label, so the graph stays legible to color blindness or a
black-and-white printout.

| State | Meaning |
|---|---|
| ⏳ Waiting | Inferred from a previous successful execution of this endpoint (the "blueprint"); the current request hasn't reached this step yet |
| ▶ Running | Currently executing |
| ✓ Completed | Finished without error, under the slow threshold |
| ⚠ Slow | Finished without error, at or above the configured slow threshold |
| ✕ Error | Finished with an exception, or a descendant step failed |
| ∅ Not executed | The blueprint expected this step, but the request finished without reaching it (e.g. an early return, a short-circuited branch) |

The blueprint of a trace is the last successful execution of the same method and route that started
before it. A trace is never drawn with steps that only a later execution ran, so an old trace looks
the same today as it did when it ended. The first execution of an endpoint has no blueprint, so it
shows no Waiting or Not executed node. If retention already removed the execution that would have
been the blueprint, the trace is drawn from the one before it, or with none.

A `Database` node also shows an N+1 badge, e.g. `⚠ ×12`, when 3 or more `Database` steps in the
same trace share the same component and operation, even under different parents. That shape is
what a classic N+1 loop produces: one query per loop iteration, same SQL format, different
instance of the step in the loop.

A finished trace also shows how much of its total duration isn't covered by any recorded step,
e.g. "30% of the time (55 ms of 183 ms) is not covered by any recorded step." The math is the
union of every step's `[startedAt, endedAt)` interval, not a naive sum of durations, so a nested
step or two parallel branches of a `Task.WhenAll` never get counted twice.

The same six states are listed in a fixed legend in the bottom-left corner of the canvas, so you
never have to recall what an icon means. The bottom-right corner has zoom in, zoom out and "fit to
screen" buttons plus the current zoom percentage. Wheel, drag and pinch keep working as before.
The buttons just make the same zoom reachable without a gesture.

While a trace is still running, a "Pause live" button appears in the graph's header. Pausing
freezes the graph where it is, so you can inspect a step without the picture moving under you.
Events arriving meanwhile are queued, not dropped, and "Resume live" applies the whole backlog in
order. It is a different thing from Replay: pausing stays on the running trace, Replay steps
through a trace that already finished.

The metrics strip above the graph carries a p95 latency card with a sparkline of the last 50
executions. The p95 figure comes from `GET /metrics` for the whole window. The sparkline is the
shape of the recent series next to it. Both refresh as traces complete.

## Try an endpoint

"Try an endpoint" (in the top bar on `/flow`, or the `/runner` page) sends a real request to your
API from the dashboard, so the graph has something to draw without you leaving the browser or
reaching for a REST client.

The panel builds its form from your application's own OpenAPI document: pick an endpoint, fill in
path and query parameters and a JSON body, send. It needs `MapOpenApi()` (or an equivalent OpenAPI
endpoint) in the host application, and says so when the document can't be loaded. The dashboard's
own routes are filtered out of the list.

When the OpenAPI document declares a security scheme (`http`/`bearer`, `apiKey`, `oauth2`,
`openIdConnect`), an "Authorize" section appears above the request form. Paste a value into its
masked field for each scheme; a preview next to it shows the exact header or query parameter that
will be sent. The right one is applied automatically to every request whose route requires that
scheme, and a header added by hand always wins over the automatic one. The pasted value lives only
in `sessionStorage` for the current tab: closing it clears the value, and nothing is written to
disk.

## Export and import a trace

A trace on `/flow` can be downloaded as a `.json` file ("Download trace (.json)") and opened again
in any other dashboard instance ("Import trace (.json)"). Both happen entirely in the browser:
exporting serializes the trace already in memory, importing reads the file locally, and neither
makes an HTTP call in either direction. The person receiving the file needs no access to the API
that produced it, nor to its database, which is what makes a trace mailable to a colleague or
pastable into a conversation with an AI agent.

An imported trace renders through exactly the same graph, waterfall, metric cards and event feed
as a live one, carries a visible "Imported trace" badge for as long as it is open, and has a
"Back to live traces" action to leave. It is never persisted: it does not enter `ITraceStore` and
never shows up in `/history`, `/insights`, `/metrics` or `/architecture`, so someone else's trace
can't contaminate your own statistics. A file that isn't a NetApiFlow export gets a clear error
message, never a blank screen.

Clicking a span of an imported trace builds its ancestors and children from the file itself, with
no round trip. That span detail has no median duration and no delta from the median: those come
from previous executions of the same operation, and the instance reading the file has no history
for a trace it never recorded.

## Compare two executions

Two executions of the same route can be compared side by side. On `/history`, select exactly two
rows and press "Compare". That opens `/diff?a=<traceIdA>&b=<traceIdB>`, a shareable URL for as
long as both executions still exist in the `ITraceStore` of whoever opens it.

Steps are matched by component and operation, never by span id (a span id is unique to one
execution and never repeats across two). A step repeated within one execution, which is what an
N+1 loop produces, is summed into a single row showing the count on each side, instead of filling
the table with near-identical rows. A step that only one of the two executions ran shows as "not
executed" on the other side, with the same icon and label the graph already uses, never as a
matched step with zero duration. Matched steps show the duration of each side and the delta
between them.

Comparing two executions of different routes still works, every step simply lands on one side
only. The page flags it as a warning, since that's rarely what you meant.

## Deploy annotations

A deploy marker on `/history` helps you correlate an incident you're investigating later with a
deploy someone else made, often on another machine, another day. Mark one right after a deploy
finishes, as a step in your release pipeline:

```bash
curl -X POST https://your-api.example.com/flow/api/deploys \
  -H "Content-Type: application/json" \
  -d '{"label": "v1.4.2"}'
```

The server assigns the timestamp; a client clock running a few seconds off never shifts a marker's
position on the timeline. The marker shows up in `/history` as a full-width row, in chronological
order among the executions around it, visible to everyone who opens the dashboard, not just the
browser that created it. There's no UI to create one from the dashboard itself in this release,
and no way to edit or delete a marker once saved.

## Dashboard preferences

The dashboard UI defaults to Portuguese (pt-BR); switch to English from the language toggle in
the top bar. It also follows your OS's light/dark preference automatically, with a manual
override next to the language toggle. Both choices persist across reloads; the displayed package
version always comes from a live `GET /config` call, so it matches whatever's actually installed.

In the history list (`/history`), every column header sorts the whole result set server-side, not
just the current page. Combine "slow only" with sorting by Duration (descending) to get the
slowest executions across the entire history.

## Insights

`/insights` is an aggregated view across every traceable route of the host API, for a chosen time
window (last 24 hours, last 7 days by default, or last 30 days): the slowest routes by p95
duration, the routes with the most errors, and dead routes, those that exist in the host API but
never ran in the chosen window. All three come from `GET /metrics/routes` and `GET /endpoints`;
slowest and top errors are two different sortings of the same payload, and dead routes is the
dashboard cross-referencing it against every route the host API exposes.

## Architecture map

`/architecture` is the static call graph of a single route (HTTP method and route template),
built from every execution recorded within a chosen time window, not just the last one: the same
question `/blueprint` answers from one trace ("what does this route normally do"), generalized to
the whole window. Pick a route and a window; a step that didn't run on every execution shows its
occurrence percentage right in its label (e.g. `TryGetAsync (14%)`), so a conditional path like a
cache hit stands out. Export the graph as Mermaid or SVG, both generated in the browser from the
data already loaded, no extra request.

> **The map reflects what was persisted, not necessarily every real execution.** With tail
> sampling (below) enabled and a low `SuccessSampleRate`, most persisted executions of a route are
> its errors and slow runs, so the map skews toward that path instead of the typical successful
> one. Expected consequence of sampling, not a bug.

## Configuration

```csharp
builder.Services.AddNetApiFlow(options =>
{
    options.Enabled = true; // master switch; false skips every check at near-zero cost
    options.SlowThreshold = TimeSpan.FromMilliseconds(250);
    options.CaptureSqlText = false; // never captures parameter values, only enables the SQL text itself
    options.Retention.MaxTraces = 1000;
    options.Retention.MaxAge = TimeSpan.FromHours(24);
    options.Limits.MaxSpansPerTrace = 300;
    options.Limits.MaxTagsPerSpan = 20; // default shown; extra tags are discarded and counted
    options.Limits.MaxTagValueLength = 4000; // default shown; a longer value is truncated, not dropped
    options.IncludePaths("/api/**"); // only trace matching routes; default traces everything
    options.ExcludePaths("/api/health"); // excluded even if it also matches an include pattern
    options.ExternalSources = ExternalActivitySource.HttpClient | ExternalActivitySource.Npgsql; // opt-in; default is None
    options.CapturePrePipelineSteps = true; // opt-in; default is false, see below
    options.CaptureMinimalApiEndpoints = true; // default shown; false asks for WithNetApiFlowTracking()
    options.TailSampling.Enabled = true; // opt-in; see Tail sampling below
    options.TailSampling.SuccessSampleRate = 0.1; // default shown
    options.LogCorrelation.Enabled = true; // opt-in; see Log correlation below
    options.Limits.MaxLogEntriesPerSpan = 20; // default shown
    options.OpenApiDocumentPath = "/swagger/v1/swagger.json"; // default is /openapi/v1.json
});
```

`OpenApiDocumentPath` tells the dashboard's "Run an endpoint" panel where your application serves
its OpenAPI document. The default is what `MapOpenApi()` serves. Point it at
`/swagger/v1/swagger.json` for Swashbuckle, or wherever else you serve it. The panel lists your
endpoints from that document, so a wrong path leaves it with nothing to show. Requests to that path
are never traced, so the document the dashboard fetches does not show up in your traces. Under a
`PathBase` see [Paths under `UsePathBase`](#paths-under-usepathbase).

`CapturePrePipelineSteps` records what runs before your controller is even called, on MVC
Controller routes: one step per Authorization filter, under the component `AuthorizationFilters`
with the filter's type name as its operation, and one step around the Resource filter stage,
under the component `ResourceFilters` with the action name as its operation. The latter becomes
the parent of the `Controller` step already in the graph, so the stages nest instead of
duplicating. That's the answer to "the request took 400 ms but my controller only took
20 ms, where did the rest go" when the time went to authorization or to a resource filter such as
output caching. It is off by default and changes nothing for anyone who hasn't turned it on.
Minimal API routes have no MVC filter stage, so they see no new step either way.

`CaptureMinimalApiEndpoints` opens the `Endpoint` step of a Minimal API endpoint with no marking. It
listens to the routing's `Microsoft.AspNetCore.Routing.EndpointMatched` diagnostic event, which the
framework writes and does not document as an API, so a future framework version could rename it.
A test in the repository fails when it stops firing, and `.WithNetApiFlowTracking()` keeps
working either way. The step is marked as failed when the endpoint throws and also when the
response is a 5xx with no exception, which `.WithNetApiFlowTracking()` does not do. `MapHealthChecks`
and static files get no step.

`ExternalSources` ingests the `Activity` instances the .NET runtime and the common database
drivers already produce, with no code change in your services.

In `Development` all three are on by default, so a new install shows outbound HTTP and SQL without
configuring anything. Everywhere else the default stays `None`. Whatever you state explicitly
wins, from configuration or from code, including setting it back to
`ExternalActivitySource.None` while developing.

`HttpClient` covers outbound calls (ADR-0012). The outbound URL is reduced to
`scheme://host[:port]/path` before it is ever recorded: query string and fragment are never
captured, same guarantee as every other span.

`Npgsql` covers SQL commands (ADR-0018), recorded as a `Database` span named after the command
type and the main table it touches, for example `SELECT Users`. When you also use
`NetApiFlow.EntityFrameworkCore`, the two never record the same command twice: the driver's own
`Activity` is dropped only when a `Database` step already wraps the call, which is exactly what
`FlowDbCommandInterceptor` opens. Inside a controller, a service, a repository or any other step,
the command is recorded as a child of that step. A manual `Database` step around direct SQL swallows
the driver's commands that run inside it. Without that package (plain Npgsql, or Dapper over
Npgsql), the driver's `Activity` is the only record of the call and is ingested normally. The full SQL text
only reaches the span when `CaptureSqlText` is enabled, the same rule the EF Core interceptor
follows.

`SqlClient` does the same for SQL Server, so a Dapper query or a raw `SqlCommand` still shows up
as a `Database` span instead of leaving a gap in the flow. It follows the same rule as the Npgsql
one against `NetApiFlow.EntityFrameworkCore` (dropped only when a `Database` step already wraps the
command), and obeys the same `CaptureSqlText` rule. One difference worth knowing: unlike Npgsql, `Microsoft.Data.SqlClient` publishes no
`ActivitySource` of its own, so NetApiFlow reads the driver's diagnostic events instead. Those
events only carry a readable payload from `Microsoft.Data.SqlClient` 6.0 on. If your application
pins an older driver, the flag is accepted but records nothing, and NetApiFlow logs that once at
`Information` level naming the version requirement, rather than leaving you with an enabled
feature and an empty flow.

Mounting the dashboard at a path other than the default, and protecting it:

```csharp
app.MapNetApiFlowDashboard("/internal/flow")
    .RequireAuthorization("AdminOnly"); // or register an IFlowAccessFilter in DI
```

Persisting to a real database instead of memory (call after `AddNetApiFlow`, it replaces the
default store):

```csharp
builder.Services.AddNetApiFlowPostgreSqlStorage(
    connectionString,
    options => options.TablePrefix = "netapiflow_"); // default shown
```

`NetApiFlow.Storage.SqlServer` mirrors the same shape (`AddNetApiFlowSqlServerStorage`).

### Paths under `UsePathBase`

`ExcludePaths`, `IncludePaths` and `OpenApiDocumentPath` value the path you see, relative to the
`PathBase`, whenever the request matches an endpoint. With `app.UsePathBase("/api")`,
`ExcludePaths("/health")` keeps `GET /api/health` out of tracing. NetApiFlow decides it when routing
picks the endpoint, which is where the path is already relative.

A request that matches no endpoint, a static file served by `UseStaticFiles` or a 404, is decided by
the full path, `PathBase` included, because there is no endpoint to read it from. Write the pattern
with the prefix: `ExcludePaths("/api/site.css")`. A static file served as an endpoint, with
`MapStaticAssets()`, is an endpoint and follows the first rule.

The routes of the dashboard (its pages, its REST API and its stream) and of the MCP server are never
traced, with or without a `PathBase`. They are told apart by what they are, not by their text. Mark
your own routes the same way, whatever the patterns say:

```csharp
app.MapHealthChecks("/healthz").WithoutNetApiFlowTracking();
app.MapGroup("/internal").WithoutNetApiFlowTracking();
```

`WithoutNetApiFlowTracking()` works on a Minimal API endpoint, on a group, on `MapControllers()` and
on `MapHealthChecks()`, and it wins over `ExcludePaths`, `IncludePaths` and `WithNetApiFlowTracking()`
on the same endpoint. A controller marked by attribute has no equivalent.

What was measured: Kestrel with and without `UsePathBase`, IIS Express with the ASP.NET Core Module
v2, in-process and out-of-process, and nginx in front of Kestrel. Where the framework sets the
`PathBase` before NetApiFlow runs (IIS) or the proxy cuts the prefix, the problem never existed: it
came from `UsePathBase` called in your own code. What was not measured: the full IIS and YARP.

### Running without DDL permission

By default the storage creates and upgrades its tables on first use, with a `CREATE TABLE`, an
`ALTER TABLE` and an `UPDATE` against your application's database. If the database user of your
application cannot run DDL, turn that off and create the tables yourself:

```csharp
builder.Services.AddNetApiFlowPostgreSqlStorage(
    connectionString,
    options => options.AutoCreateSchema = false);
```

With `AutoCreateSchema = false` nothing is created or changed. On first use the storage only checks
that the tables and the `route_key` column exist. If something is missing it logs an error that says
what is missing and where the script is, and it does not take your request down. Run `schema.sql`
yourself: `src/NetApiFlow.Storage.PostgreSql/schema.sql` or
`src/NetApiFlow.Storage.SqlServer/schema.sql` in the repository, at the tag of the version you
installed (`https://github.com/Douglasproglima/netapiflow/tree/v<version>/src`). If you changed
`TablePrefix`, replace `netapiflow_` in the script first. The script creates what is missing and
upgrades tables written by an earlier version, and it is safe to run again. The SQL Server script
separates its batches with `GO`, so run it with `sqlcmd`, SSMS or Azure Data Studio.

Until the tables exist, the traces your application produces cannot be saved. A save that fails
logs a warning with the type and the message of the exception, and the next trace is tried
normally. After three failures in a row, the storage discards the traces that arrive for 30
seconds and tries again, with one warning for each attempt that fails. The traces that were not
saved are counted in `netapiflow.persistence.failures`. When a save works again it logs one line
with how many traces were not saved. The same happens when the database is unreachable. The full
exception is logged at `Debug`.

## Dashboard security in production

> **The dashboard shows internal implementation details of your API: component names, operation
> names, SQL table names, error messages.** Treat its exposure with the same care as any other
> internal diagnostics endpoint.
>
> Outside `IHostEnvironment.IsDevelopment()`, every dashboard route (REST, SSE and the UI itself)
> responds **404** unless you explicitly configure one of:
>
> - `.RequireAuthorization("YourPolicy")`, chained on `app.MapNetApiFlowDashboard(...)`.
> - A custom `IFlowAccessFilter` registered in DI, checked on every request
>   regardless of environment.
>
> There is no "partially open" default. If neither is configured, production always 404s.
>
> **An app with a `FallbackPolicy` that requires an authenticated user** (common in JWT APIs) gets
> **401** on the dashboard instead, in every environment: the authorization middleware answers
> before the dashboard's own check, and a browser does not send a bearer token when it navigates
> to `/flow`. In Development, let the dashboard through:
>
> ```csharp
> var dashboard = app.MapNetApiFlowDashboard();
> if (app.Environment.IsDevelopment())
> {
>     dashboard.AllowAnonymous();
> }
> ```
>
> Outside Development, either chain `.RequireAuthorization("YourPolicy")` and nothing else, or
> chain `.AllowAnonymous()` and register an `IFlowAccessFilter`, which then decides every request.
> Chaining `.AllowAnonymous()` together with `.RequireAuthorization()` authorizes nothing:
> `AllowAnonymous` wins over the policy, so without an `IFlowAccessFilter` every route answers 404.
>
> When the application starts with a `FallbackPolicy` and a dashboard that has neither
> `.AllowAnonymous()` nor `.RequireAuthorization()`, NetApiFlow logs one warning that names the 401
> and the way out, so it does not stay unexplained.

### Which `Host` and `Origin` are accepted

The dashboard and the MCP endpoint check the `Host` and the `Origin` of every request, on every
method: the REST API, the live stream, the files and the MCP transport. The check runs after your
own pipeline, so `UseForwardedHeaders` has already set the `Host`. A refused request gets a **403**
whose message says why and what to change.

- **In Development**, the `Host` must be `localhost`, `127.0.0.1` or `[::1]`, or a name you declare
  in the `AllowedHosts` setting. A `"*"` does not count as declaring. This is what stops DNS
  rebinding: a page on another site whose own name resolves to your machine reaches the dashboard
  with that name as the `Host`, so only the `Host` can refuse it.
- **In every environment**, a request with an `Origin` must have the host and the port of the `Host`
  the application sees. The scheme is not compared. A request without an `Origin` passes, such as a
  `GET` from the same page or a client that is not a browser, and an `Origin: null` is refused.
- **Outside Development**, the `Host` follows your own `AllowedHosts` and `UseForwardedHeaders`.

To open the dashboard in Development by a name other than localhost, for example from a phone
through the address of your machine on the local network, or `host.docker.internal`, declare it:

```json
{ "AllowedHosts": "localhost;192.168.1.13" }
```

An entry is a name without a port, and `*.example.com` matches subdomains. ASP.NET Core reads the same
setting in its own host filtering, so once the list names specific hosts, keep `localhost` in it or
the application answers 400 to `localhost` itself.

Behind a reverse proxy the application has to see the original `Host`. Otherwise the `Origin` the
browser sends does not match it and a `DELETE` gets 403. Keep the `Host` in the proxy
(`proxy_set_header Host $http_host;` in nginx), or call `UseForwardedHeaders` with
`ForwardedHeaders.XForwardedHost` before the dashboard and let the proxy send `X-Forwarded-Host`.
A proxy that ends TLS needs nothing for the scheme, because it is not compared.

### The trace id header

NetApiFlow writes `X-NetApiFlow-TraceId` on the response of every traced request in Development. It
carries the id of the trace, a random id the server makes for each request. It is not the W3C trace
id of the `traceparent` header: that one is in the tag `w3c.trace_id` of the trace and in
`Activity.Current.TraceId`, and the details of a trace in the dashboard show both. It is how
`NetApiFlow.Testing` finds the trace of a response and how the dashboard's Runner opens the graph of
the request it sent. Knowing a trace id does not open the dashboard: its routes stay behind the
rules above.

**Before 0.6.0 the id of the trace was the W3C trace id.** Two requests with the same `traceparent`
shared one id and lost a trace. If a test or a log line compared `Flow.CurrentTraceId` or this
header with `Activity.Current.TraceId`, read the W3C trace id from the tag instead.

**From 0.6.0 the header is sent only in Development by default. To keep it in Production, set
`EmitTraceIdHeader = true`.**

```csharp
builder.Services.AddNetApiFlow(options => options.EmitTraceIdHeader = true);
```

Or in `appsettings.json`: `"NetApiFlow": { "EmitTraceIdHeader": true }`. Set it to `false` to keep
the trace id out of your responses in Development too. Requests are traced either way.
`GetFlowTrace` from `NetApiFlow.Testing` reads this header, so it throws while the header is off. A
test host built with `WebApplicationFactory` runs in Development and needs nothing. If your tests run
in another environment, such as `Testing` or `Staging`, set `EmitTraceIdHeader = true` in the test
host.

One response does not carry it even with the header on. In Production with no exception handler,
an exception that nothing handles reaches the server, and the server builds the 500 itself and
drops the headers set before. That 500 has no `X-NetApiFlow-TraceId`, although the trace is
recorded. Measured on Kestrel, on
`net8.0` and `net10.0`. NetApiFlow does not change that response. Add `UseExceptionHandler` and the
500 is answered inside your pipeline, so it carries the header. In Development the developer
exception page does that for you.

### Security headers

Every response of the dashboard and of the MCP endpoint carries `X-Content-Type-Options: nosniff`,
`Referrer-Policy: no-referrer`, `X-Frame-Options: DENY` and `Content-Security-Policy: frame-ancestors
'none'`. That includes the 403 and the 404 of the access check. No other page can show the dashboard
in a frame, so nothing can hide its delete button under a click.

The page of the dashboard itself carries a full policy:

```text
default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; base-uri 'self'; object-src 'none'; form-action 'self'; frame-ancestors 'none'
```

`script-src` has no `'unsafe-inline'`: the dashboard runs only the script files it ships. `style-src`
has it, on purpose. Angular adds the styles of each component as `<style>` elements when the page
runs, there are dozens of them, and a hash for each would change with every build. An inline style
cannot run code. If your own middleware sets a `Content-Security-Policy` on these routes, the one of
the dashboard replaces it.

## MCP server

```bash mcp:package
dotnet add package NetApiFlow.Mcp
```

Add these two lines at the top of `Program.cs`, next to the `using NetApiFlow;` the Quick start
already has:

```csharp mcp:usings
using NetApiFlow.Mcp;
using NetApiFlow.Mcp.Endpoints;
```

Then register the server before `builder.Build()` and mount it right after:

```csharp mcp:registration
builder.Services.AddNetApiFlowMcp();

var app = builder.Build();
app.MapNetApiFlowMcp(); // mounted at /flow/mcp by default
```

Three read-only tools, backed by the same `ITraceStore` the dashboard reads from: `list_recent_traces`,
`get_trace` and `get_component_stats`. No write tool exists in this version: nothing deletes a
trace or triggers a replay through MCP.

> **Same security rule as the dashboard.** Outside `IHostEnvironment.IsDevelopment()`, every MCP
> route responds **404** unless you explicitly configure `.RequireAuthorization(...)` or a custom
> `IFlowAccessFilter`, exactly like [Dashboard security in production](#dashboard-security-in-production).
> An AI agent reading trace data is the same exposure risk as the visual dashboard.
>
> The three tools exist only on the NetApiFlow endpoint. An app that hosts its own MCP server
> (`AddMcpServer()` and `MapMcp(...)`) does not serve them on its own endpoint, so they stay behind
> the rule above, and `/flow/mcp` does not serve the tools of the app. `/flow/mcp` answers only the
> three tools and the handshake. Prompts, resources, completions and any other method of the app get
> `-32601 Method '...' is not available`, even when the `initialize` of the app announces those
> capabilities. One configuration is the
> exception. When the MCP server of the app keeps sessions (a `SessionMode` other than `Stateless`)
> and sets `PerSessionExecutionContext = true`, a tool call does not carry the HTTP request that sent
> it, so NetApiFlow cannot tell which endpoint it came from. The NetApiFlow tools are then served on
> no endpoint, `/flow/mcp` answers 404, and the app logs one warning at startup that says so. Set
> `PerSessionExecutionContext = false`, or use the stateless mode, to get them back.
>
> The endpoint also refuses a request whose `Host` or `Origin` is not its own, with a **403**. In
> Development the `Host` must be `localhost`, `127.0.0.1`, `[::1]` or a name declared in
> `AllowedHosts`, and an `Origin` must match the `Host` the application sees. An MCP client that is
> not a browser, such as the MCP Inspector or the official C# client, sends no `Origin`, so it is
> not affected. The details, and what to set behind a reverse proxy, are in
> [Which `Host` and `Origin` are accepted](#which-host-and-origin-are-accepted). Its responses carry
> the same [security headers](#security-headers) as the dashboard's.

See `samples/SampleApi.Controllers/README.md` for how to point a real MCP client (e.g. Claude
Code) at a running instance.

## OTLP export

```bash
dotnet add package NetApiFlow.OpenTelemetry
```

Add `using NetApiFlow.OpenTelemetry;` at the top of `Program.cs`, next to the `using NetApiFlow;` the
Quick start already has, then register the export before `builder.Build()`:

```csharp
builder.Services.AddNetApiFlowOtlpExport();
```

The NetApiFlow core already produces real `Activity` instances on the `"NetApiFlow"`
`ActivitySource` (ADR-0002). `AddNetApiFlowOtlpExport` points the OpenTelemetry SDK at that
source and exports it via OTLP, to any compatible backend: Jaeger, Tempo, Aspire Dashboard,
Honeycomb, or an OpenTelemetry Collector. There is no NetApiFlow-specific serialization: the SDK
exports the same `Activity` the dashboard reads from.

**If your app already configures OpenTelemetry, use `AddNetApiFlowSource()` and not
`AddNetApiFlowOtlpExport`.** `AddNetApiFlowOtlpExport` adds its exporter to the tracer provider of
your app. In that app, every source your app traces also goes to the NetApiFlow endpoint, and that
can send data to a destination you did not expect. `AddNetApiFlowSource()` adds the `"NetApiFlow"`
source to the tracing you already have, and nothing else: no exporter and no other source. The
steps then go to the exporters you already set up. Such an app already has `using OpenTelemetry.Trace;`
at the top of `Program.cs`. Add `using NetApiFlow.OpenTelemetry;` next to it:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("MyApp")
        .AddNetApiFlowSource()
        .AddOtlpExporter());
```

Use `AddNetApiFlowOtlpExport` when NetApiFlow is all your app exports and you do not want to set up
OpenTelemetry yourself.

```csharp
builder.Services.AddNetApiFlowOtlpExport(options =>
{
    options.IncludeHttpClientSource = true; // default: false, see the warning below
    options.ConfigureOtlpExporter = otlp => otlp.Endpoint = new Uri("http://localhost:4317");
});
```

> **`IncludeHttpClientSource` exports the full outbound URL, including query string.** Enabling
> it also observes the native `"System.Net.Http"` `ActivitySource`, mirroring
> `NetApiFlowOptions.ExternalSources` so the exported trace has the same outbound HTTP spans the
> dashboard shows. That native `Activity` never passes through NetApiFlow's own URL sanitizer
> (which only sanitizes `FlowSpan`), so the OTLP backend receives the URL as-is. Default is
> `false` for this reason.

> **Exported volume can exceed what the dashboard shows.** The OpenTelemetry SDK samples
> independently of NetApiFlow's own sampling decision (tracing disabled, the 500-trace limit
> reached). A request NetApiFlow discards early can still be exported. Neither pipeline blocks
> the other; this is the expected result of two independent `ActivityListener` instances
> observing the same source, not a bug.

## Tail sampling

```csharp
builder.Services.AddNetApiFlow(options =>
{
    options.TailSampling.Enabled = true; // default: false, every completed trace is persisted
    options.TailSampling.SuccessSampleRate = 0.1; // default shown, keep 10% of the rest
});
```

Disabled by default: every completed trace is persisted, same as before this option existed.
Enabled, the decision is made once, when a trace is ready to persist, before it ever reaches the
`ITraceStore`:

- A trace with an error, or whose request timed out while the client stayed connected, is always
  persisted. A client disconnecting before the request finished does not count as an error here:
  it follows the same sampling as a successful trace, since a client giving up says nothing about
  the server.
- A trace at or above `SlowThreshold` is always persisted (the same threshold already used to
  color the UI, not a second concept of "slow").
- Any other trace (successful, not slow, not a client disconnect) is persisted with probability
  `SuccessSampleRate`.

> **The live view never changes.** `GET /traces/live` and the SSE stream read from the in-memory
> recorder, not from the store, so a sampled-out trace still shows up while it runs. Only the
> persisted history is affected.

> **`/blueprint`, `/architecture` and the aggregated metrics reflect the sample, not the real
> traffic, once enabled.** A low `SuccessSampleRate` can leave `/blueprint` without a recent
> successful trace to replay, skews `/architecture` toward the error and slow executions that are
> always kept, and `/metrics`, `/metrics/components` and `/metrics/routes` count only what was
> kept. This is the expected result of sampling, not a bug. `GET /config` exposes the current
> state so `/history` shows a visible banner whenever tail sampling is on.

## Log correlation

```csharp
builder.Services.AddNetApiFlow(options =>
{
    options.LogCorrelation.Enabled = true; // default: false
    options.LogCorrelation.MinLevel = LogLevel.Warning; // default shown
});
```

Disabled by default. Enabled, every `ILogger` call made at or above `MinLevel` while a `FlowStep`
is active is correlated to that span's `Logs`, right next to its error, no second tool to open to
see what the application logged while that step ran. The mechanism is a second `ILoggerProvider`,
registered alongside whatever providers your app already has (Console, Application Insights,
etc.): it never replaces or filters them, logging to your usual destinations keeps working
exactly as before. A log call outside a tracked step (startup, an uninstrumented background job)
is never observed by this provider.

**With Serilog, pass `writeToProviders: true`.** `builder.Host.UseSerilog(...)` replaces the
logger factory and, by default, does not hand log events to the other registered providers, so
the trace shows no logs even though the console still prints them:

```csharp
builder.Host.UseSerilog(
    (context, configuration) => configuration.WriteTo.Console(),
    writeToProviders: true);
```

> **Sanitization here is the same best-effort regex already used for `FlowStep.MarkError`, not a
> new or stronger guarantee.** A log message is free text your own code writes, and can in
> principle contain anything, including data the regex-based sanitizer doesn't recognize. This is
> why the feature defaults to off and `MinLevel` defaults to `Warning`: lower the blast radius
> before you decide the trade-off is worth it for your application.

## Sensitive data

NetApiFlow never captures, under any configuration:

- Request or response bodies.
- Method arguments or return values.
- Headers or query strings (routes are always recorded as templates, e.g. `/api/orders/{id}`,
  never the resolved URI).
- SQL parameter values, even with `CaptureSqlText = true` (that flag only captures the command
  text and the main table it touches).
- Query strings or fragments of outbound URLs, when `ExternalSources` is enabled (same reduction
  to `scheme://host[:port]/path` as routes).
- Connection string, database user or peer address, when `ExternalActivitySource.Npgsql` is
  enabled. The driver reports all of them on its own `Activity`; none is ever copied onto a span.

> **A request that never matches a route is recorded as `(unmatched)`, never its real path.** A
> 404 on a typo'd URL and a successful request to a static file both land in history as
> `(unmatched)`, indistinguishable from each other. That's the same template guarantee above, just
> for the case where there's no template to use yet. If static assets or your own OpenAPI document
> are cluttering your history with `(unmatched)` noise, `ExcludePaths` keeps them out of tracing
> entirely.
>
> A request that matches no route is recorded only when it ends. While it is in progress it is not
> listed as running, and nothing it does shows up before then, such as an EF Core or `HttpClient`
> call inside a terminal middleware or a WebSocket handler. A request that matches an endpoint
> shows as running from the moment routing picks it.

Error messages are sanitized and then truncated to 300 characters, in every output: the dashboard,
the MCP tools, the stored trace, correlated logs and the `netapiflow.error.message` tag on the
`Activity`, which is what an OpenTelemetry exporter sends. Sanitizing first means a secret cut by
the limit leaves nothing behind.

The sanitizer is on by default and it is a filter for known shapes, not a guarantee that no secret
survives. What it removes: e-mail addresses, CPF, CNPJ, long numeric sequences, a JWT (three
base64url segments starting with `eyJ`), the password in a URL (`scheme://user:password@host`), and
the value of any `key=value`, `key: value` or JSON `"key": "value"` pair whose key contains
`password`, `passwd`, `pwd`, `token`, `secret`, `api-key` (also `api_key` and `apikey`),
`passphrase`, `private_key` or `authorization`, in any case. A key inside brackets, such as
`config[password]`, counts. The Azure keys `AccountKey`, `SharedAccessKey`,
`SharedAccessSignature` count too, and so does a `sig=` in a signed URL.

How a value ends:

- A quoted value is removed whole, escaped quotes included. When it is a quoted string in JSON, the
  JSON around it stays valid.
- The value of `Authorization` includes its scheme (`Bearer`, `Basic`, `Digest`, AWS) and its
  parameters. A `Cookie:` or `Set-Cookie:` header loses the rest of the line.
- A bare `Bearer <token>` is removed.
- An unquoted value ends at a space or at `;`, like a connection string pair. So `password=ab;cd`
  leaves `;cd`.

Any message with one of those words followed by `:` or `=` loses what comes after it, even a
harmless one such as `Unexpected token: '}'`, because a secret cannot be told apart from it and
leaking one is the worse failure. For the same reason `the bearer of the card` loses the word after
`bearer`. See [`docs/adr/0007-nunca-capturar-payload.md`](docs/adr/0007-nunca-capturar-payload.md).

What it does not touch, or touches only in part:

- `Basic <base64>` on its own, without `Authorization`.
- `credentials: ...`, as in `Invalid credentials: ana`.
- A cookie in `name=value` form without the `Cookie:` prefix, such as `.AspNetCore.Cookies=...`.
- An unquoted value with a space: it is cut at the first space, so `password=two words` leaves
  `words`. Only a quoted value can hold spaces.
- A JSON value that is not a string: `{"token":12345}` becomes `{"token":[redacted]`, and the closing
  brace goes with it.
- A password with a `@` that is not percent-encoded in a URL: `postgres://u:p@ss@host/db` leaves what
  follows the first `@`.

The one exception is what you write yourself: a step's tags (see
[Tagging a step](#tagging-a-step)) are stored and served exactly as given, with no sanitization.

## Performance and overhead

Measured with `tests/NetApiFlow.Benchmarks` (BenchmarkDotNet), comparing the cost of a request
against a minimal ASP.NET Core pipeline with NetApiFlow **absent** (package not installed),
**disabled** (`Enabled = false`) and **enabled** (default options), on two endpoints: `Simple`
(no steps marked) and `Nested` (four nested `Flow.Step` calls, mirroring a
Controller→Service→Repository→Database chain).

| Scenario | Simple (mean) | Nested, 4 steps (mean) |
|---|---|---|
| Absent (package not installed) | 6.22 μs | 11.79 μs |
| Disabled (`Enabled = false`) | 9.17 μs | 12.84 μs |
| Enabled (default options) | 27.00 μs | 43.84 μs |

So tracing a request with default options costs roughly 20 μs on an endpoint with no steps
marked, and about 32 μs more with four nested steps, on this hardware. Turning tracing off
(`Enabled = false`) keeps most of that cost away (listener registration itself has a small,
near-constant overhead) without removing the package.

Method: each scenario (absent/disabled/enabled) runs as its own process, isolating .NET's
process-wide `ActivitySource` listener registration so one scenario can't bias another. Full
BenchmarkDotNet reports, including hardware and runtime details, are generated under
`tests/NetApiFlow.Benchmarks/BenchmarkDotNet.Artifacts/results/` on every run.

## Known limitations

- `Activity.Current` (and therefore step nesting) is lost across `ExecutionContext.SuppressFlow()`
  or a custom, non-flowing thread pool. Spans created after such a boundary start a new root
  activity, and NetApiFlow does not attach them to the trace of the request.
- A step started with an explicit `ActivityContext` as its parent, in a thread that does not carry
  the `ExecutionContext` of the request, is not part of any trace. Before 0.6.0 it was recorded,
  because it shared the W3C trace id of the request. Start it inside the flow of the request, or set
  `Activity.Current` to an activity of the request first.
- Automatic tracking (`AddFlowTracked`, `DecorateFlowTracked` and `TrackFlowByConvention`) does not support `IAsyncEnumerable<T>`
  return types in this version.
- Automatic tracking does not work under Native AOT either. `AddFlowTracked`,
  `DecorateFlowTracked` and `TrackFlowByConvention` build their proxy with `DispatchProxy`, which
  emits a type at run time, and the last two also match implementation types by reflection. All
  three carry `[RequiresDynamicCode]` (and the last two also `[RequiresUnreferencedCode]`), so you
  get an `IL3050`/`IL2026` warning at build time instead of a failure at startup. The marking form that
  stays safe under Native AOT and trimming is `Flow.Step(...)`: a host calling only
  `AddNetApiFlow()` and `Flow.Step(...)` publishes with zero AOT or trimming warnings on net8.0 and
  net10.0, and there is a check in the repository that proves it. The same check proves that a
  Minimal API endpoint becomes an `Endpoint` step in the native binary.
- The `NetApiFlow.EntityFrameworkCore` interceptor is a separate question from the one above. The
  interceptor itself emits no warning, but EF Core states it is not fully compatible with Native
  AOT, and a published host using it reports that: `IL2026`/`IL3050` on the `DbContext`
  constructor and `IL2104`/`IL3053` on the EF Core assemblies. That limitation belongs to EF Core,
  not to NetApiFlow, and no change here can remove it. Under Native AOT, mark database work with
  `Flow.Step(...)` if you want a warning-free publish.
- The live view is per process, not per cluster. `GET /traces/live` reads the in-memory recorder of
  whichever replica answered that call, so behind a load balancer without sticky sessions `/flow`
  shows only the traffic that one replica handled, and the browser can land on a different replica
  on the very next request. Persisted history does not have this problem: with a storage package
  configured every replica writes to the same database, so `GET /traces/{traceId}` and the History
  screen find a trace no matter which replica recorded it.
- `net8.0` left Microsoft's official support window on 10 November 2026, and NetApiFlow still ships
  an asset for it. It stops shipping in the first version published on or after 10 May 2027. If you
  still need it after that, stay on the last version that has it. Keeping it until then does mean
  the package has a target framework that no longer gets servicing updates from Microsoft, which is
  worth knowing if your own policy forbids that.
- A `net11.0` app uses the `net10.0` asset. The CI runs the whole test suite on the .NET 11 runtime,
  on Linux, against the build of `net10.0`, and the storage package was tested against a real SQL
  Server with `Microsoft.Data.SqlClient` 7.1.2. An asset of its own for `net11.0` will exist only on
  the day the code uses an API that only .NET 11 has.
- `net9.0` (STS) is not a supported target. Multi-targeting covers `net8.0` and `net10.0` (both
  LTS); a `net9.0` consumer resolves to the `net8.0` asset, including the hand-written SSE
  implementation used there instead of `net10.0`'s native `TypedResults.ServerSentEvents`.

## Contributing

Issues and pull requests are welcome. This project follows Conventional Commits and Gitflow. See
`docs/gitflow.md` if you're sending a PR.

## Support

NetApiFlow is free and open source. If it saved you time, consider buying the author a coffee:
PIX key `douglasproglima@gmail.com`.

## License

MIT. See [LICENSE](LICENSE).

## Inspiration

NetApiFlow is inspired by the idea behind [apiflow](https://github.com/joshuamaia/apiflow) (Spring
Boot): visualizing a request's path through an API in real time. No code, file or asset from
that project was copied or ported. Everything here is written from scratch, for .NET.
