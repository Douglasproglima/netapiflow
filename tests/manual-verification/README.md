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
dotnet pack NetApiFlow.slnx -c Release -o tests/manual-verification/local-nuget-feed
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
dotnet pack NetApiFlow.slnx -c Release -o tests/manual-verification/local-nuget-feed
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

## Native AOT manual-path verification

The sibling check above proves the two proxy-based forms warn. This one checks the other half of
the same sentence: the README recommends `NetApiFlow.Step(...)` and the
`NetApiFlow.EntityFrameworkCore` interceptor as the forms that stay safe under Native AOT.
`AotManualPathVerification` uses only those two, never `AddFlowTracked`/`DecorateFlowTracked`.

```bash
cd tests/manual-verification/AotManualPathVerification
dotnet publish -c Release
```

On Windows the ILC step shells out to the MSVC linker through `vswhere.exe`. If the publish ends
in `MSB3073` naming `link.exe`, add the installer directory to `PATH` (typically
`C:\Program Files (x86)\Microsoft Visual Studio\Installer`) and run it again. That failure is a
local toolchain gap, not a result.

**The target is zero AOT or trimming warnings, and it is not met today.** The warnings are the
point of the check, so none of them are suppressed here. Current state:

- Three call sites in `src/NetApiFlow/Configuration/NetApiFlowOptionsSetup.cs` (lines 14, 16 and
  22) each report both `IL2026` and `IL3050`, six warnings in total. They come from
  `ConfigurationBinder.Bind` and `ConfigurationBinder.Get<string[]>`, which bind
  `NetApiFlowOptions` from the `NetApiFlow` configuration section. `AddNetApiFlow()` registers that
  binder unconditionally, so every Native AOT host gets these six whether or not it uses a
  configuration file. Reproduced with nothing but `AddNetApiFlow()` and `Step(...)` in the host,
  no EF Core referenced at all.
- `NetApiFlow.Step(...)` itself contributes no warning.
- The EF Core half brings `IL2026`/`IL3050` on the `DbContext` constructor, `IL2104`/`IL3053` on
  `Microsoft.EntityFrameworkCore`, `.Relational` and `.Sqlite`, and four `IL3002`. None originate
  in `NetApiFlow.EntityFrameworkCore`, but the interceptor cannot be used without EF Core, which
  documents itself as not fully compatible with Native AOT.
