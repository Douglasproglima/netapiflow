# Dashboard package verification (tarefa 5.6)

Proves the six packed `.nupkg` work standalone, the way a real consumer installs them: no
`ProjectReference` into the rest of the repo, no NetApiFlow.slnx. `DashboardPackageVerification`
is a runnable host calling `UseNetApiFlowDashboard` at `/flow` and at `/qualquer/caminho`;
`DashboardPackageVerification.Tests` checks both with `HttpClient`.

## Running it

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
