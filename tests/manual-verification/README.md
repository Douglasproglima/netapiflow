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
git-ignored repo-wide) — delete and re-pack it after any change to a package project, since
nothing here rebuilds it automatically.

### Testing from an external project

A project outside this repository (no `ProjectReference`, no clone of this repo) can consume
`local-nuget-feed/` directly. Skip `dotnet nuget add source`: `dotnet add package --source`
resolves its argument as a literal path or URI, never as a name registered with
`dotnet nuget add source -n`.

```bash
dotnet add package NetApiFlow --version <version> --source "<absolute-path-to>/local-nuget-feed"
dotnet add package NetApiFlow.Dashboard --version <version> --source "<absolute-path-to>/local-nuget-feed"
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
