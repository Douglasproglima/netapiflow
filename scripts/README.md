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
- `verify-live-panel.mjs` opens the panel of the built app in a headless Chrome and reads the nodes it
  draws. `tests/real-package/user-code/LiveProbeEndpoints.cs` gives it an endpoint that runs one step,
  and then either returns or waits at a gate that the script opens, so a request is held by the gate and
  never by a sleep. It runs three checks: `live-panel:old-trace` (an older trace is not redrawn with the
  steps of a later execution as not executed), `live-panel:running` (a request that is still running
  draws the steps it is expected to run as waiting, with its route known) and `live-panel:ended` (the same
  steps are drawn as not executed once the request ended without them). The panel follows the language of
  the browser, so a node is read as waiting or not executed in English and in Portuguese. Run it by hand
  with `node scripts/verify-live-panel.mjs --dll <app>/bin/Release/net10.0/UserApp.dll`.

## The built demo in a browser

The job `built-demo` of `ci.yml` builds the static demo (`npm run build:demo`) and opens it in a headless
Chrome, the one the runner already has, so no browser is downloaded and the scripts need no dependency.
`verify-built-demo.mjs` serves `dist/demo/browser` with `Cache-Control: no-store`, so the browser cannot show
an old build, opens the trace of **every** `api/traces/<id>` the manifest lists, and the screens `/history`,
`/components` and `/runner`, and fails on any console error, any uncaught exception, any response with an
error status, any request that never gets an answer and any page that does not draw what marks it as ready
(the graph for a trace, the table, the cards or the form for a screen). It prints
`0 errors in N of N` for the traces and for the screens, and it also fails when it visited fewer traces than
the manifest lists, so a loop that skips some cannot pass.

`browser-session.mjs` talks to the browser over the Chrome DevTools Protocol with the `WebSocket` of Node. It
looks for `google-chrome`, `chromium` or Edge, or uses `CHROME_PATH`, and passes `--no-sandbox` unless you pass
`--sandbox`. It removes the profile folder it created and never one that you gave with `--profile-directory`.

```bash
cd src/NetApiFlow.Dashboard/ClientApp && npm run build:demo && cd -
node scripts/verify-built-demo.mjs --directory src/NetApiFlow.Dashboard/ClientApp/dist/demo/browser
```

The demo answers a dashboard call that it has no recording for with a 404 that never leaves the browser, so
it is silent unless the demo says so. It does: `demo-fixture.interceptor.ts` writes
`The demo has no recording for <url>` to the console, which is what the check reads. The `Vitest` spec
`demo-fixture-coverage.spec.ts` covers the manifest itself: every file it cites exists, and each blueprint key
points at the last successful trace of the same method and route that started strictly before it, or, for a key
in `noContent`, there is none.

## The regression guards

`check-regression-guards.mjs` reads `tests/regression-guards/guards.json` (see the `README.md` next to it)
and checks, after one run of the tests, that each guard it lists is still there and passed. Run it by
hand after `dotnet test ... --logger "trx;LogFileName=<project>.trx" --results-directory <folder>` for the
projects that the rows name:

```bash
node scripts/check-regression-guards.mjs --trx <folder> --vitest <report.json>
```

The Vitest report comes from `npm run test:ci -- --reporters default --reporters json --output-file <report.json>`.

## The comparison with the published package

The second job of `real-package.yml`, `comparison`, runs the same scenarios against two builds of the same
app, `tests/real-package/comparison-app/`: one compiled against the packages of the build (the feed of the
job) and one against the last published version, which `tests/real-package/previous-published-version.txt`
pins and the CI restores from nuget.org. It is how a regression of behavior shows, the kind a test written for
the new code does not see.

- `build-comparison-apps.mjs` copies the app twice outside the repository, adds `NetApiFlow` and
  `NetApiFlow.Dashboard` with an exact version to each, and builds both.
- `compare-with-published.mjs` starts both apps for each variant of configuration (no `PathBase`, with
  `PathBase`, `ExcludePaths`, `IncludePaths`, an exception handler, a status code page, Production), sends the
  same requests to both, and compares what each one answered and recorded: the status, whether the
  `X-NetApiFlow-TraceId` header is there, and the traces (method, route and status), in the live list while a
  request is held and in the list after. A request is held by a gate of the app (`/__probe/...`), never by a
  sleep. It clears the traces before each scenario, after it waited for a trace of its own to be stored, so a
  write that is late cannot leak into the first one.
- `tests/real-package/expected-differences.json` lists each difference this build is known to have, with the
  scenario, the aspect, the value of each side, the reason and the text of the CHANGELOG `[Unreleased]` entry
  that announces it. **A difference that is not in the list fails. A line of the list that does not appear any
  more fails too**, so the list cannot rot. A line whose CHANGELOG text is not in `[Unreleased]` fails as well.

To see what the differences are now, for example when you add a scenario, run with `--print`. A difference
without a CHANGELOG entry that can announce it is a regression, not a line for the list.

```bash
node scripts/compare-with-published.mjs --new-dll <new>/ComparisonApp.dll --old-dll <old>/ComparisonApp.dll --print
```

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
