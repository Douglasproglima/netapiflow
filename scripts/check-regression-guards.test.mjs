import assert from 'node:assert/strict';
import { test } from 'node:test';
import { checkGuard, checkRegistry, parseNodeTest, parseTrx, parseVitest, structuralProblems } from './check-regression-guards.mjs';

const TRX = `<?xml version="1.0" encoding="UTF-8"?>
<TestRun>
  <Results>
    <UnitTestResult executionId="1" testId="1" testName="Tests.HostTests.Refuses_a_foreign_host" outcome="Passed" />
    <UnitTestResult executionId="2" testId="2" testName="Tests.HostTests.Theory(host: &quot;evil.example&quot;)" outcome="Passed" />
    <UnitTestResult executionId="3" testId="3" testName="Tests.HostTests.Theory(host: &quot;a.example&quot;)" outcome="Failed" />
    <UnitTestResult executionId="4" testId="4" testName="Tests.HostTests.Skipped_one" outcome="NotExecuted" />
  </Results>
</TestRun>`;

const VITEST = {
  testResults: [
    {
      name: 'C:\\repo\\ClientApp\\src\\app\\core\\demo-fixture-coverage.spec.ts',
      assertionResults: [
        { fullName: 'the blueprints of the recorded demo points a at b', status: 'passed' },
        { fullName: 'the blueprints of the recorded demo points c at d', status: 'passed' },
        { fullName: 'the manifest of the recorded demo cites only files the fixtures hold', status: 'skipped' },
      ],
    },
  ],
};

const WINDOWS_PATH = 'C:/repo/scripts/compare-with-published.test.mjs'.replaceAll('/', String.fromCharCode(92));

const NODE_TEST = `<?xml version="1.0" encoding="utf-8"?>
<testsuites>
  <testcase name="a renamed CHANGELOG is still read" time="0.001" classname="test" file="/repo/scripts/compare-with-published.test.mjs"/>
  <testcase name="the Unreleased section is announced &quot;twice&quot;" time="0.001" classname="test" file="${WINDOWS_PATH}"/>
  <testcase name="a test that fails" time="0.001" classname="test" file="/repo/scripts/compare-with-published.test.mjs">
    <failure type="testCodeFailure" message="boom">boom</failure>
  </testcase>
  <testcase name="a test that was skipped" time="0" classname="test" file="/repo/scripts/compare-with-published.test.mjs">
    <skipped type="skipped" message="skipped"/>
  </testcase>
</testsuites>`;

function sources(overrides = {}) {
  return {
    dotnet: new Map([['Tests', parseTrx(TRX)]]),
    vitest: parseVitest(VITEST),
    nodeTest: parseNodeTest(NODE_TEST),
    scenarios: { 'real-package': new Set(['a', 'd', 'nuspec:NetApiFlow']), comparison: new Set(['status-page']), 'built-demo': new Set(['screens']) },
    ...overrides,
  };
}

function dotnetGuard(test) {
  return { id: 'S6', defect: 'd', fixedIn: '#1', job: 'build-test', guard: { kind: 'dotnet', project: 'Tests', test } };
}

function vitestGuard(test) {
  return { id: 'F1', defect: 'd', fixedIn: '#1', job: 'build-test', guard: { kind: 'vitest', file: 'src/app/core/demo-fixture-coverage.spec.ts', test } };
}

function nodeTestGuard(test) {
  return { id: 'N1', defect: 'd', fixedIn: '#1', job: 'build-test', guard: { kind: 'node-test', file: 'scripts/compare-with-published.test.mjs', test } };
}

function scenarioGuard(job, scenario) {
  return { id: 'Q', defect: 'd', fixedIn: '#1', job, guard: { kind: 'ci-scenario', scenario } };
}

test('parseTrx_ReadsTheNameAndTheOutcomeOfEachResult_AndDecodesEntities', () => {
  const results = parseTrx(TRX);

  assert.equal(results.length, 4);
  assert.deepEqual(results[1], { name: 'Tests.HostTests.Theory(host: "evil.example")', outcome: 'Passed' });
});

test('parseVitest_KeepsTheFileWithForwardSlashes', () => {
  const [first] = parseVitest(VITEST);

  assert.equal(first.file, 'C:/repo/ClientApp/src/app/core/demo-fixture-coverage.spec.ts');
  assert.equal(first.status, 'passed');
});

test('checkGuard_ForADotnetTestThatPassed_ReportsNothing', () => {
  assert.equal(checkGuard(dotnetGuard('Tests.HostTests.Refuses_a_foreign_host'), sources()), undefined);
});

test('checkGuard_ForADotnetTestThatWasDeletedOrRenamed_SaysItIsMissing', () => {
  assert.match(checkGuard(dotnetGuard('Tests.HostTests.Refuses_a_foreign_origin'), sources()), /no test .* in the results \(deleted or renamed\?\)/);
});

test('checkGuard_ForADotnetTestThatWasSkipped_FailsAndSaysItGuardsNothing', () => {
  assert.match(checkGuard(dotnetGuard('Tests.HostTests.Skipped_one'), sources()), /NotExecuted, not Passed \(a skipped test guards nothing\)/);
});

test('checkGuard_ForATheory_ChecksEveryCaseOfIt', () => {
  assert.match(checkGuard(dotnetGuard('Tests.HostTests.Theory'), sources()), /Failed, not Passed/);
});

test('checkGuard_ForADotnetTestWhoseProjectIsNotInTheRun_SaysSo', () => {
  assert.match(checkGuard(dotnetGuard('Tests.HostTests.Refuses_a_foreign_host'), sources({ dotnet: new Map() })), /results of Tests are not in the run/);
});

test('checkGuard_ForAVitestTestThatPassed_ReportsNothing', () => {
  assert.equal(checkGuard(vitestGuard('the blueprints of the recorded demo points'), sources()), undefined);
});

test('checkGuard_ForAVitestTestThatWasSkipped_Fails', () => {
  assert.match(checkGuard(vitestGuard('the manifest of the recorded demo cites only files'), sources()), /skipped, not passed/);
});

test('checkGuard_ForAVitestTestThatIsGone_SaysItIsMissing', () => {
  assert.match(checkGuard(vitestGuard('the blueprints that were deleted'), sources()), /no test .* in the results/);
});

test('checkGuard_ForAVitestGuardWhenThereIsNoVitestReport_DoesNotJudgeIt', () => {
  assert.equal(checkGuard(vitestGuard('anything'), sources({ vitest: undefined })), undefined);
});

test('parseNodeTest_ReadsTheNameTheFileAndTheOutcomeOfEachTestCase_AndDecodesEntities', () => {
  const results = parseNodeTest(NODE_TEST);

  assert.deepEqual(
    results.map((result) => result.outcome),
    ['passed', 'passed', 'failed', 'skipped'],
  );
  assert.equal(results[1].name, 'the Unreleased section is announced "twice"');
  assert.equal(results[1].file, 'C:/repo/scripts/compare-with-published.test.mjs');
});

test('checkGuard_ForANodeTestThatPassed_ReportsNothing', () => {
  assert.equal(checkGuard(nodeTestGuard('a renamed CHANGELOG is still read'), sources()), undefined);
});

test('checkGuard_ForANodeTestOfAFileRunOnWindows_FindsItByTheForwardSlashPath', () => {
  assert.equal(checkGuard(nodeTestGuard('the Unreleased section is announced "twice"'), sources()), undefined);
});

test('checkGuard_ForANodeTestThatWasDeletedOrRenamed_SaysItIsMissing', () => {
  assert.match(checkGuard(nodeTestGuard('a CHANGELOG that was renamed again'), sources()), /no test .* in the results \(deleted or renamed\?\)/);
});

test('checkGuard_ForANodeTestThatFails_FailsAndNamesTheOutcome', () => {
  assert.match(checkGuard(nodeTestGuard('a test that fails'), sources()), /failed, not passed/);
});

test('checkGuard_ForANodeTestThatWasSkipped_FailsAndSaysItGuardsNothing', () => {
  assert.match(checkGuard(nodeTestGuard('a test that was skipped'), sources()), /skipped, not passed \(a skipped test guards nothing\)/);
});

test('checkGuard_ForANodeTestWhenThereIsNoNodeTestReport_SaysSo', () => {
  assert.match(checkGuard(nodeTestGuard('anything'), sources({ nodeTest: undefined })), /no node test report/);
});

test('checkGuard_ForAScenarioOfAJob_ReportsNothingWhenItExists', () => {
  assert.equal(checkGuard(scenarioGuard('real-package', 'd'), sources()), undefined);
  assert.equal(checkGuard(scenarioGuard('real-package', 'nuspec:NetApiFlow'), sources()), undefined);
});

test('checkGuard_ForAScenarioThatIsGone_SaysTheJobHasNoSuchScenario', () => {
  assert.match(checkGuard(scenarioGuard('real-package', 'z'), sources()), /the job real-package has no scenario "z"/);
});

test('structuralProblems_ReportsAMissingFieldADuplicateIdAnUnknownKindAndAnUnknownJob', () => {
  const problems = structuralProblems([
    { id: 'A', defect: 'd', fixedIn: '#1', job: 'build-test', guard: { kind: 'dotnet', project: 'p', test: 't' } },
    { id: 'A', defect: 'd', fixedIn: '#1', job: 'build-test', guard: { kind: 'dotnet', project: 'p', test: 't' } },
    { id: 'B', defect: 'd', fixedIn: '#1', job: 'nowhere', guard: { kind: 'dotnet', project: 'p', test: 't' } },
    { id: 'C', defect: 'd', fixedIn: '#1', job: 'build-test', guard: { kind: 'manual' } },
    { id: 'D', fixedIn: '#1', job: 'build-test', guard: { kind: 'dotnet', project: 'p', test: 't' } },
  ]);

  assert.equal(problems.length, 4);
  assert.ok(problems.some((problem) => problem.includes('appears twice')));
  assert.ok(problems.some((problem) => problem.includes('"nowhere"')));
  assert.ok(problems.some((problem) => problem.includes('"manual"')));
  assert.ok(problems.some((problem) => problem.includes('the field "defect" is missing')));
});

test('checkRegistry_ListsTheProblemOfEveryGuardThatFailed', () => {
  const problems = checkRegistry(
    [
      dotnetGuard('Tests.HostTests.Refuses_a_foreign_host'),
      { ...dotnetGuard('Tests.HostTests.Skipped_one'), id: 'S6b' },
      { ...scenarioGuard('real-package', 'z'), id: 'Q2' },
    ],
    sources(),
  );

  assert.equal(problems.length, 2);
  assert.ok(problems[0].startsWith('S6b:'));
  assert.ok(problems[1].startsWith('Q2:'));
});
