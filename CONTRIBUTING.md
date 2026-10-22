# Contributing to NetApiFlow

Thanks for considering a contribution. NetApiFlow is a personal open source project; this
document explains how to build it locally and how to send a change.

## Local setup

Backend (run from the repository root):

```bash
dotnet restore NetApiFlow.slnx
dotnet build NetApiFlow.slnx
dotnet test NetApiFlow.slnx
dotnet format NetApiFlow.slnx
```

Dashboard frontend (run from `src/NetApiFlow.Dashboard/ClientApp/`, available from Phase 4 on):

```bash
npm ci
npm start
npm run test:ci
npm run lint
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
   changes, and (if the dashboard changed) the Angular lint, test and build.
3. Fill in the PR template: what changed, why, and how to verify it.

## Code style

- C# and TypeScript rules are in `.editorconfig` and `.claude/rules/`. In short: `Nullable`
  enabled, warnings as errors, public members documented with XML comments in English.
- Keep the core package (`NetApiFlow`) free of third-party dependencies beyond
  `Microsoft.AspNetCore.App`.

## Reporting a security issue

Do not open a public issue. See [SECURITY.md](SECURITY.md).
