# Repository scripts

Run the unit tests of the scripts with `node --test scripts/*.test.mjs`. Node only, no dependency.

## The real-package job

`.github/workflows/real-package.yml` is the job that uses NetApiFlow the way a person who just found it
does. `ci.yml` and `release.yml` both call it, and the release waits for it. It packs the packages
with `-p:Version=<base>-ci.<run number>` into a folder outside the repository, makes a new
`dotnet new webapi` app, pastes the README code into it, builds it, runs it, and walks an HTTP script.

- `build-user-app.mjs` reads the blocks the README marks on the opening line of the fence
  (`csharp quickstart:program`, `csharp quickstart:registrations`, `csharp mcp:usings`,
  `csharp mcp:registration`, `bash quickstart:packages`, `bash mcp:package`) and builds `Program.cs`
  from them without editing any block. The MCP registration block is split on the `var app = builder.Build();`
  line, because it crosses it. The script fails when a marker is gone, so a README edit cannot switch the
  check off, and when `README.pt-BR.md` shows different code from `README.md` (a trailing `// comment` is
  ignored). What the README leaves to the reader comes from `tests/real-package/user-code/`: the
  `IOrderService` and `IOrderRepository` interfaces with their classes, two endpoints (one throws),
  and an exception handler that is on only with `USER_EXCEPTION_HANDLER=true`. The packages come from the job's feed with an exact version, so a
  version that is not in the feed fails the restore instead of falling back to nuget.org.
- `package-scenarios.mjs` starts the built app in four variants (`Development`, `Production`, and
  `Production` with `EmitTraceIdHeader` with and without an exception handler) and runs the scenarios
  that apply to each. `--list` prints them. `--base-url <url> --variant <name> --repeat <n>` runs one
  variant against an app that is already standing, which is how to check that a scenario is stable.
  A new scenario goes in the `SCENARIOS` list, with the promise it comes from in its title.

## `nuspec-snapshot.mjs`

Reads the `.nuspec` of every `.nupkg` in a folder and writes what matters about its dependencies in a
normal form: the package id, then for each target framework every dependency with its version range,
then the framework references. The version of the package itself is replaced by `{version}`, so only a
real change shows. `tests/nuspec-snapshot/<PackageId>.txt` holds the approved form of each package.

```bash
for project in src/*/*.csproj; do dotnet pack "$project" -c Release -p:Version=0.0.0-snapshot.1 -o /tmp/feed; done
node scripts/nuspec-snapshot.mjs --feed /tmp/feed
node scripts/nuspec-snapshot.mjs --feed /tmp/feed --update
```

Without `--update` it compares, prints the diff of what differs, and exits with 1. A package with no
approved file, and an approved file with no package, also fail. Pack all the packages before comparing,
or the approved files of the others count as packages that went missing.

## `generate-demo-fixtures.mjs`

Records the dashboard's own REST answers from a running sample app into the fixture set the
static demo build serves. Nothing is edited by hand, so the demo cannot drift from the real
contract.

Start the sample first, in `Development` (outside it the dashboard answers 404 by design):

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:60803 \
  dotnet run --project samples/SampleApi.Controllers -f net10.0 --no-launch-profile

node scripts/generate-demo-fixtures.mjs --base-url http://localhost:60803
```

It exercises the demo scenarios a few times, waits for the persistence queue to report them,
then captures every route `ApiService` calls plus the host's own OpenAPI document.

It also sends one `GET` to every route of the sample listed in that OpenAPI document and records
the answer (status, content type, body) together with the `X-NetApiFlow-TraceId` the host
returned. That is what the Runner receives in the static demo, and why each recorded trace
belongs to a response the Runner can follow. The recorder stops with an error when a route
answers without that header, so a sample started with `EmitTraceIdHeader` off never produces a
demo that cannot open a graph.

The recorder formats the JSON it writes with the Prettier of the dashboard, so run `npm ci` in
`src/NetApiFlow.Dashboard/ClientApp` once before running it. Start the sample fresh each time:
traces live in memory, and a sample that already served requests puts them into the history.

`--dashboard` defaults to `/flow` and rarely needs passing. On Git Bash for Windows, a leading
slash in an argument is rewritten into a Windows path (`/flow` becomes `C:/Program Files/Git/flow`),
so pass it as `--dashboard flow` if you ever need to.

### Serving the result

```bash
cd src/NetApiFlow.Dashboard/ClientApp
npm run build:demo
npx http-server dist/demo/browser -c-1 --proxy "http://localhost:8080?"
```

The `--proxy` argument is what makes unknown paths fall back to `index.html`. Without it,
`http-server` answers 404 for every route except `/`, the app never boots on a deep link, and the
page looks empty rather than broken. Any static host needs the same fallback (on GitHub Pages
that means a `404.html` copy of `index.html`).
