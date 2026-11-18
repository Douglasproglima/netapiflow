# Repository scripts

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
