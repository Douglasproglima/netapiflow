## What

<!-- What does this PR change, in one or two sentences. -->

## Why

<!-- Why this change is needed. Link an issue if there is one. -->

## How to verify

<!-- Exact commands to run to verify the change. -->

## Checklist

- [ ] Branch name follows `docs/gitflow.md` (`feature/*`, `bugfix/*`, `chore/*`, `release/*`, `hotfix/*`)
- [ ] PR title follows Conventional Commits
- [ ] Tests added or updated for the change
- [ ] `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` pass locally
- [ ] `npm run lint`, `npm run test:ci`, `npm run build` pass locally (if `ClientApp/` changed)
- [ ] If the PR fixes a defect, a row in `tests/regression-guards/guards.json` names the test that guards it, or the PR says that it has none
- [ ] If the PR changes a contract, its body has a "Consumidores do contrato" section that says affected or not affected, with the proof, for each consumer in `.claude/rules/contratos-e-consumidores.md`
