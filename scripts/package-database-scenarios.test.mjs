import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CHECK_IDS, checkSeveralCommands, KINDS } from './package-database-scenarios.mjs';

const CONTROLLER_SPAN_ID = 'c0';
const BEFORE = { label: 'before', id: '00-trace-span-01', isStopped: false };

function detailFor(kind, overrides = {}) {
  const { component, tableQualifier } = KINDS[kind];
  const command = (sequence, table, extra = {}) => ({
    sequence,
    spanId: `s${sequence}`,
    parentSpanId: CONTROLLER_SPAN_ID,
    component,
    type: 'Database',
    operation: `SELECT ${table}`,
    status: 'Success',
    tags: { sql: `SELECT count(*) FROM ${tableQualifier}${table}` },
    ...extra,
  });

  return {
    spans: [
      { sequence: 1, spanId: CONTROLLER_SPAN_ID, parentSpanId: null, component: 'Controller', type: 'Controller', status: 'Success' },
      command(2, 'users'),
      command(3, 'orders'),
      command(4, 'users'),
      { sequence: 5, spanId: 's5', parentSpanId: CONTROLLER_SPAN_ID, component: 'Service', type: 'Service', status: 'Success' },
    ],
    ...overrides,
  };
}

function bodyOf(entries = []) {
  return JSON.stringify([BEFORE, ...['users', 'orders', 'users'].map((label) => ({ ...BEFORE, label })), ...entries]);
}

for (const kind of Object.keys(KINDS)) {
  test(`checkSeveralCommands_ForTheThreeCommandsOfAMethod_AcceptsOneStepEachOn_${kind}`, () => {
    assert.doesNotThrow(() => checkSeveralCommands(detailFor(kind), kind, bodyOf()));
  });

  test(`checkSeveralCommands_WhenTheCommandsCollapseIntoFewerSteps_FailsAndSaysWhatItSaw_${kind}`, () => {
    const detail = detailFor(kind);
    detail.spans = detail.spans.filter((span) => span.sequence !== 3 && span.sequence !== 4);

    assert.throws(() => checkSeveralCommands(detail, kind, bodyOf()), /steps are \[SELECT users\], expected one step for each/);
  });

  test(`checkSeveralCommands_WhenACommandIsNotAChildOfTheController_Fails_${kind}`, () => {
    const detail = detailFor(kind);
    detail.spans[3].parentSpanId = 's2';

    assert.throws(() => checkSeveralCommands(detail, kind, bodyOf()), /not a successful child of the controller/);
  });

  test(`checkSeveralCommands_WhenTheSqlWasNotCaptured_Fails_${kind}`, () => {
    const detail = detailFor(kind);
    detail.spans[1].tags = {};

    assert.throws(() => checkSeveralCommands(detail, kind, bodyOf()), /the SQL of the steps is/);
  });

  test(`checkSeveralCommands_WhenTheStepAfterTheCommandsHangsUnderACommand_Fails_${kind}`, () => {
    const detail = detailFor(kind);
    detail.spans[4].parentSpanId = 's4';

    assert.throws(() => checkSeveralCommands(detail, kind, bodyOf()), /opened after the commands is not a child of the controller/);
  });

  test(`checkSeveralCommands_WhenTheCurrentActivityMovedAfterACommand_Fails_${kind}`, () => {
    const moved = JSON.stringify([BEFORE, { ...BEFORE, label: 'users', id: '00-trace-other-01' }]);

    assert.throws(() => checkSeveralCommands(detailFor(kind), kind, moved), /current activity of the caller changed after users/);
  });
}

test('CHECK_IDS_NameEveryScenarioOfEveryKind_AndOnlyThoseOfTheRegressionGuards', () => {
  assert.ok(CHECK_IDS.includes('database:sqlserver:several-commands'));
  assert.ok(CHECK_IDS.includes('database:postgres:restart'));
  assert.equal(new Set(CHECK_IDS).size, CHECK_IDS.length);
  assert.equal(CHECK_IDS.length, Object.keys(KINDS).length * 4);
});
