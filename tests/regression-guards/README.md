# Regression guards

`guards.json` is the list of the defects that were found and fixed, each with the test or the check
that goes red if the defect comes back. It exists so that a guard cannot be deleted, renamed or
skipped without the CI saying so.

Every row has:

- `id`: a short name, unique in the file.
- `defect`: one line that says what was wrong.
- `fixedIn`: the pull request or the release that fixed it.
- `guard`: what protects it, one of three kinds:
  - `dotnet`: `project` (the name of the test project) and `test` (the full name of the test method, with
    its namespace and class). A `[Theory]` is guarded as a whole: every case has to pass.
  - `vitest`: `file` (the spec, from the `ClientApp` folder) and `test` (the start of the name of the
    test, `describe` and `it`, as the report writes it).
  - `ci-scenario`: `scenario`, the id of a scenario of a job. The scenarios are the ones of
    `scripts/package-scenarios.mjs` and the `a` of `scripts/build-user-app.mjs` for `real-package`, one
    `nuspec:<PackageId>` for each file of `tests/nuspec-snapshot/`, the ids of
    `scripts/compare-with-published.mjs` for `comparison`, and the checks of
    `scripts/verify-built-demo.mjs` for `built-demo`.
- `job`: the job that runs it (`build-test`, `real-package`, `comparison` or `built-demo`).

## What the CI does with it

The step "Check that every regression guard still exists and passes" of `build-test` reads the `.trx`
file of each test project and the JSON report of the dashboard tests, which the job already produces in
one run, and fails when:

- a listed test is not in the results, because it was deleted or renamed;
- a listed test is in the results but did not pass, and a skipped test counts as not passed;
- a listed scenario is not in the scenarios of its job;
- a row has a missing field, an id that appears twice, or a kind or a job that does not exist.

The dashboard report exists only when the dashboard changed. In a run without it the `vitest` rows are not
judged, which is safe: a pull request that does not touch `ClientApp` cannot delete or skip a test there.

## When you fix a defect

Add a row for it in the same pull request, with the test that was red before the fix. If the guard is a
test that you rename later, change its row in the same commit and say why in the pull request.

## When you cannot add a row

A defect that has no guard does not get a row, and no test is written in a hurry to fill one. Say in the
pull request that it has none, so that the audit can decide when it is closed.
