# Manual verification

Checks that need a project outside `NetApiFlow.slnx` to prove anything, so they are not part of
`dotnet test NetApiFlow.slnx` and have to be run by hand.

## Dashboard package verification

Proves the six packed `.nupkg` work standalone, the way a real consumer installs them: no
`ProjectReference` into the rest of the repo, no NetApiFlow.slnx. `DashboardPackageVerification`
is a runnable host calling `UseNetApiFlowDashboard` at `/flow` and at `/qualquer/caminho`;
`DashboardPackageVerification.Tests` checks both with `HttpClient`.

### Running it

From the repo root:

```bash
dotnet pack NetApiFlow.slnx -c Release -o tests/manual-verification/local-nuget-feed -p:Version=<next-version>-verify.<n>
cd tests/manual-verification
dotnet test DashboardPackageVerification.Tests
```

To open it in a browser instead:

```bash
cd tests/manual-verification/DashboardPackageVerification
dotnet run
```

Then visit `http://localhost:<port>/flow` and `http://localhost:<port>/qualquer/caminho`.

`local-nuget-feed/` is resolved via `nuget.config` in this folder (`*.nupkg` is already
git-ignored repo-wide). Delete and re-pack it after any change to a package project, since
nothing here rebuilds it automatically.

A fake version number (never the real one) is never optional, on any `dotnet pack` under this
folder. Packing with the real version number (the one that is about to be, or already was,
published) occupies that exact slot in the machine-wide NuGet cache
(`~/.nuget/packages/<id>/<versão>/`) with the local build. Every `dotnet add package`/restore
afterward, on this machine, for that id and version, then resolves the local build instead of the
real one on nuget.org, silently, even after the real one is published. Happened twice already
(`0.4.0`, then the `0.4.2` hotfix verification), same cause both times. If a cache folder under
the real version number turns up contaminated, delete it before trusting anything checked against
it.

**`-p:VersionSuffix=verify` does not do this here.** `Directory.Build.props` sets `<Version>`
directly, and the SDK only composes `$(Version)` from `$(VersionPrefix)`/`$(VersionSuffix)` when
`<Version>` is not already set, which is never the case in this repository. Every pack that used
`-p:VersionSuffix=verify` produced the real version number anyway, silently. Use
`-p:Version=<next-version>-verify.<n>` instead, which overrides `<Version>` for real; never reuse
the same `<n>` while evaluating the same upcoming version.

The same `nuget.config` points `globalPackagesFolder` at `local-packages/`, so these projects
extract packages into this folder instead of the machine-wide one. Re-packing without bumping the
version would otherwise restore the previously extracted content for that id and version, and the
check would keep passing against the build before the change.

### Testing from an external project

A project outside this repository (no `ProjectReference`, no clone of this repo) can consume
`local-nuget-feed/` directly. Skip `dotnet nuget add source`: `dotnet add package --source`
resolves its argument as a literal path or URI, never as a name registered with
`dotnet nuget add source -n`.

```bash
dotnet add package NetApiFlow --version <version> --source "<absolute-path-to>/local-nuget-feed"
dotnet add package NetApiFlow.Dashboard --version <version> --source "<absolute-path-to>/local-nuget-feed"
```

## MediatR package verification

`MediatR` 12.5.0 added an optional `CancellationToken` parameter to `RequestHandlerDelegate<T>`.
That compiles against either version, but an assembly built against the older signature cannot
find the method at run time, so `FlowTrackingBehavior` threw `MissingMethodException` on the first
request through the pipeline. A `ProjectReference` recompiles and never sees it;
`MediatRPackageVerification.Tests` installs the packed `NetApiFlow.MediatR` from the local feed and
pins `MediatR` above the version it was compiled against, which is what a consumer's restore does.

```bash
dotnet pack NetApiFlow.slnx -c Release -o tests/manual-verification/local-nuget-feed -p:Version=<next-version>-verify.<n>
cd tests/manual-verification
dotnet test MediatRPackageVerification.Tests
```

## Native AOT annotation verification

`AddFlowTracked` and `DecorateFlowTracked` build their proxy with `DispatchProxy`, which emits a
type at run time and cannot work under Native AOT. `AotAnnotationVerification` is a
`PublishAot=true` host that calls both, so the trimming analyzer has to report them. It is kept out
of `NetApiFlow.slnx` on purpose: the whole point is that it compiles with warnings, and
`TreatWarningsAsErrors` would turn them into a broken build everywhere else.

```bash
cd tests/manual-verification/AotAnnotationVerification
dotnet build
```

Three warnings are expected, and no fewer. Silence means an annotation was dropped from
`FlowTrackedServiceCollectionExtensions`:

- `IL3050` on `AddFlowTracked`
- `IL3050` and `IL2026` on `DecorateFlowTracked`

This one uses a `ProjectReference` instead of the local feed. The analyzer reads the attributes
straight from the IL, so a project and a `.nupkg` give the same answer, and this way the check runs
without packing anything first.

## Native AOT core verification

The opposite claim to the one above: a host that marks steps the recommended way has to publish
clean. `AotCoreVerification` calls only `AddNetApiFlow()` and `NetApiFlow.Step(...)`, no proxy
form, no EF Core.

```bash
cd tests/manual-verification/AotCoreVerification
dotnet publish -c Release
```

**Zero AOT or trimming warnings are expected.** `EnableConfigurationBindingGenerator` in
`NetApiFlow.csproj` is what makes this true: without it, binding `NetApiFlowOptions` goes through
reflection and reports six warnings (`IL2026`/`IL3050` on three `ConfigurationBinder` call sites),
whether or not the host uses a configuration file at all. Before this generator, every Native AOT
host calling `AddNetApiFlow()` alone got those six, no matter what else it did.

## Native AOT manual-path verification

The sibling check above proves `NetApiFlow.Step(...)` alone publishes clean. This one checks the
other half of the same README sentence: it also recommends the `NetApiFlow.EntityFrameworkCore`
interceptor as a form that stays safe under Native AOT. `AotManualPathVerification` adds that
interceptor on top of `AddNetApiFlow()` and `NetApiFlow.Step(...)`, never
`AddFlowTracked`/`DecorateFlowTracked`.

```bash
cd tests/manual-verification/AotManualPathVerification
dotnet publish -c Release
```

**The core half of this check is clean now; the EF Core half never will be.** EF Core states it is
not fully compatible with Native AOT, and a host using the interceptor reports `IL2026`/`IL3050`
on the `DbContext` constructor plus `IL2104`/`IL3053` on the EF Core assemblies, four `IL3002`
besides. None of those originate in `NetApiFlow.EntityFrameworkCore`; the interceptor cannot be
used without EF Core, so they cannot be removed from this check either.

On Windows the ILC step behind both `dotnet publish` commands above shells out to the MSVC linker
through `vswhere.exe`. If the publish ends in `MSB3073` naming `link.exe`, add the installer
directory to `PATH` (typically `C:\Program Files (x86)\Microsoft Visual Studio\Installer`) and run
it again. That failure is a local toolchain gap, not a result.

## Options set only in configuration

Proves, with real packages, that `LogCorrelation.Enabled` and `CapturePrePipelineSteps` set only in
the `NetApiFlow` section of `appsettings.json` turn their features on, and that nothing runs when
they are absent. It is also the release smoke: `AddNetApiFlow()` with no lambda, in `Development`,
with SQL Server storage and `Microsoft.Data.SqlClient` 7.1.2 referenced by the app.
`OptionsFromConfigVerification` has a controller (pre-pipeline steps only exist in MVC), an
authorization filter, and two endpoints: `/api/smoke` writes a warning inside a
`NetApiFlow.Step(...)` and runs a `SqlCommand`, and `/mini/plain` runs a `SqlCommand` from a Minimal
API endpoint outside any step.

From the repo root, with `<n>` a number not used before for that version (never the real version
number, and never `-p:VersionSuffix`, which this repository ignores):

```bash
dotnet pack NetApiFlow.slnx -c Release -o tests/manual-verification/local-nuget-feed -p:Version=0.5.0-verify.<n>
docker run -d --name smoke-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Smoke050!Pass' -p 14330:1433 mcr.microsoft.com/mssql/server:2022-latest
MSYS_NO_PATHCONV=1 docker exec smoke-sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Smoke050!Pass' -Q "CREATE DATABASE smoke"
cd tests/manual-verification/OptionsFromConfigVerification
bash verify.sh net11.0 on 0.5.0-verify.<n>
bash verify.sh net11.0 control 0.5.0-verify.<n>
bash verify.sh net8.0 on 0.5.0-verify.<n>
bash verify.sh net8.0 control 0.5.0-verify.<n>
```

`net11.0` resolves the `lib/net10.0` assets and `net8.0` the `lib/net8.0` ones. The script prints the
resolved asset per package, so that is checked from `project.assets.json`, not assumed. Leave
`DOCKER_HOST` unset for the Docker CLI. Then remove the container and the `verify` packages from
`local-nuget-feed` and from `local-packages`, and commit none of them.

**Expected, `on`:** the controller trace has `AuthorizationFilters` and `ResourceFilters` spans, the
warning shows up on the step's span, the Minimal API trace has a `SqlClient` span, the trace is
still listed after restarting the app, the dashboard answers 200, and `--vulnerable` finds nothing.

**Expected, `control`:** the same app with the two keys absent. No `AuthorizationFilters` or
`ResourceFilters` span and no correlated log.

**Expected, `on` and `control`:** the `SqlCommand` of `/api/smoke` produces a `SqlClient` span whose
`parentSpanId` is the `spanId` of the `SmokeController`/`Get` step. `verify.sh` prints both. In both
modes the source is on by default, because the app runs in `Development`.

## SQL commands inside a step, Npgsql

Proves, with real packages, that a command issued by Npgsql inside a controller or a step is recorded
as a child of that step, and that an EF Core query with the interceptor is still recorded once.
`NpgsqlCommandInsideStepVerification` runs `AddNetApiFlow()` with no lambda, in `Development`, with
PostgreSQL storage, against Postgres 16 and `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4. It has
four routes:

| Route | Shape | Expected in the trace |
|---|---|---|
| `/api/raw` | controller, `NpgsqlCommand` directly | one `Npgsql` span, child of the controller step |
| `/api/step` | controller, `NpgsqlCommand` inside `NetApiFlow.Step(...)` | one `Npgsql` span, child of that step |
| `/mini/step` | Minimal API, `NpgsqlCommand` inside `NetApiFlow.Step(...)` | one `Npgsql` span, child of that step |
| `/api/ef` | controller, EF Core with the package's interceptor | exactly one `Database` span, `EntityFrameworkCore`, and no `Npgsql` span |

From the repo root, with `<n>` a number not used before for that version (never the real version
number, never `-p:VersionSuffix`):

```bash
dotnet pack NetApiFlow.slnx -c Release -o tests/manual-verification/local-nuget-feed -p:Version=0.5.0-verify.<n>
docker run -d --name smoke-pg -e POSTGRES_PASSWORD=pw -p 54320:5432 postgres:16
cd tests/manual-verification/NpgsqlCommandInsideStepVerification
bash verify.sh net11.0 on 0.5.0-verify.<n>
bash verify.sh net11.0 control 0.5.0-verify.<n>
bash verify.sh net8.0 on 0.5.0-verify.<n>
bash verify.sh net8.0 control 0.5.0-verify.<n>
```

`net11.0` resolves the `lib/net10.0` assets and `net8.0` the `lib/net8.0` ones, printed from
`project.assets.json`. `verify.sh` prints each trace's spans with their `spanId` and `parentSpanId`,
then checks that the trace survives a restart, that the dashboard answers 200, and runs
`dotnet list package --vulnerable --include-transitive` without any filter. Remove the container and
the `verify` packages afterwards and commit none of them.

**Control:** the same app with `ExternalSources` set to `None` in the configuration. No `Npgsql` span
on the first three routes, and `/api/ef` still has the interceptor's span.
