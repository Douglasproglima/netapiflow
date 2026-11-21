import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CHECKS, CHECK_IDS, stateOfNode, statesOf } from './verify-live-panel.mjs';

test('each check has its own id, and the ids are the ones the regression registry points to', () => {
  assert.deepEqual(CHECK_IDS, ['live-panel:old-trace', 'live-panel:running', 'live-panel:ended']);
  assert.equal(new Set(CHECK_IDS).size, Object.keys(CHECKS).length);
});

test('a node waiting for its step is read as waiting in English and in Portuguese', () => {
  assert.equal(stateOfNode('OrderRepository Find Waiting'), 'waiting');
  assert.equal(stateOfNode('OrderRepository Find Aguardando'), 'waiting');
});

test('a node that never ran is read as not executed in English and in Portuguese', () => {
  assert.equal(stateOfNode('OrderRepository Find Not executed'), 'not-executed');
  assert.equal(stateOfNode('OrderRepository Find Não executado'), 'not-executed');
});

test('a node that ran or is running is neither waiting nor not executed', () => {
  assert.equal(stateOfNode('OrderCache TryGet 3 ms Success'), 'other');
  assert.equal(stateOfNode('OrderCache TryGet Running'), 'other');
});

test('the states of a drawing keep the order of its nodes', () => {
  assert.deepEqual(statesOf(['OrderCache TryGet 3 ms', 'OrderRepository Find Aguardando', 'Gateway Send Not executed']), [
    'other',
    'waiting',
    'not-executed',
  ]);
});
