# Contributing to NetApiFlow

Thanks for considering a contribution. NetApiFlow is a personal open source project; this
document explains how to build it locally and how to send a change.

## Prerequisites

- The .NET SDK that `global.json` asks for (10.0.100 or a later feature band). The projects build for
  `net8.0` and `net10.0`, so the tests need both runtimes installed.
- Node, in the version of `src/NetApiFlow.Dashboard/ClientApp/.nvmrc` (`nvm use` inside that folder).
  The dashboard package embeds the built Angular app. In a new clone, the tests whose host uses the
  dashboard fail with `Could not load the embedded file manifest` until you run `npm ci` and
  `npm run build` in `ClientApp`.
- Docker, for the tests that start PostgreSQL and SQL Server in containers
  (`NetApiFlow.Storage.Tests`, `NetApiFlow.Npgsql.IntegrationTests`, `NetApiFlow.Npgsql8.IntegrationTests`,
  `NetApiFlow.Npgsql10.IntegrationTests` and `NetApiFlow.SqlClient.IntegrationTests`). Without Docker those
  five projects cannot start their containers.

## Local setup

Dashboard frontend first (run from `src/NetApiFlow.Dashboard/ClientApp/`):

```bash
npm ci
npm run build
```

Backend (run from the repository root):

```bash
dotnet restore NetApiFlow.slnx
dotnet build NetApiFlow.slnx
dotnet test NetApiFlow.slnx
dotnet format NetApiFlow.slnx
```

Frontend commands, from `src/NetApiFlow.Dashboard/ClientApp/`:

```bash
npm start
npm run test:ci
npm run lint
npm run format:check
```

## Branching and commits

This repository follows a classic Gitflow with SemVer, documented in
[docs/gitflow.md](docs/gitflow.md). In short:

- Branch from `develop`: `feature/<name>`, `bugfix/<name>`, or `chore/<name>`.
- Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/), in English
  (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`), with an optional scope
  (`feat(core): ...`).
- PR titles follow the same format; they become the squash commit message on `develop`.

## Opening a pull request

1. Push your branch and open a PR against `develop`.
2. Make sure CI is green: build and test on Linux and Windows, `dotnet format` with no pending
   changes, and (if the dashboard changed) the Angular lint, format check, test and build.
3. Fill in the PR template: what changed, why, and how to verify it.

## Code style

- C# and TypeScript rules are in `.editorconfig` and `.claude/rules/`. In short: `Nullable`
  enabled, warnings as errors, public members documented with XML comments in English.
- Keep the core package (`NetApiFlow`) free of third-party dependencies beyond
  `Microsoft.AspNetCore.App`.

## Public API

Each package lists its public surface in two files next to its `.csproj`:
`PublicAPI.Shipped.txt`, what the last release published, and `PublicAPI.Unshipped.txt`, what the next
release adds. The build fails with `RS0016` when a public type or member is in no file, and with `RS0017`
when a listed one is gone or no longer public. That is how a change to the public API becomes a diff of a
file in the pull request.

- **To add a public member**, copy the symbol that the error prints to `PublicAPI.Unshipped.txt`, or use
  the quick fix of your IDE. `dotnet format` does not apply it.
- **To rename or remove one**, write the old line again with `*REMOVED*` in front of it in
  `PublicAPI.Unshipped.txt`, and add the new line if there is one.
- **At a release**, the lines of `Unshipped` move into `Shipped`.

Do not add a public member to the surface just to make the build pass. Make it `internal`, or say why it has
to be public in the pull request.

## Reporting a security issue

Do not open a public issue. See [SECURITY.md](SECURITY.md).
