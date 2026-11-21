import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createPageWatcher, describeEvent, summarize, tracesOfManifest } from './verify-built-demo.mjs';

const TRACE_ID = '4bf92f3577b34da6a3ce929d0e0e4736';

test('tracesOfManifest_ReturnsOnlyTheIdsOfTheTraceFixtures', () => {
  const manifest = {
    routes: {
      'api/config': 'config.json',
      'api/traces': 'traces.json',
      [`api/traces/${TRACE_ID}`]: 'trace.json',
      'api/blueprint?method=GET&route=/orders': 'blueprint.json',
    },
  };

  assert.deepEqual(tracesOfManifest(manifest), [TRACE_ID]);
});

test('describeEvent_ForAConsoleError_ReturnsItsText', () => {
  const problem = describeEvent({ method: 'Runtime.consoleAPICalled', params: { type: 'error', args: [{ value: 'Failed to load' }] } });

  assert.equal(problem, 'console.error: Failed to load');
});

test('describeEvent_ForAConsoleWarning_ReturnsNothing', () => {
  assert.equal(describeEvent({ method: 'Runtime.consoleAPICalled', params: { type: 'warning', args: [{ value: 'careful' }] } }), undefined);
});

test('describeEvent_ForAnUncaughtException_ReturnsItsDescription', () => {
  const problem = describeEvent({ method: 'Runtime.exceptionThrown', params: { exceptionDetails: { text: 'Uncaught', exception: { description: 'TypeError: x is undefined' } } } });

  assert.equal(problem, 'pageerror: TypeError: x is undefined');
});

test('describeEvent_ForAnErrorStatus_ReturnsTheStatusAndTheUrl', () => {
  const problem = describeEvent({ method: 'Network.responseReceived', params: { response: { status: 404, url: 'http://localhost/assets/demo-fixtures/x.json' } } });

  assert.equal(problem, 'HTTP 404: http://localhost/assets/demo-fixtures/x.json');
});

test('describeEvent_ForASuccessfulResponse_ReturnsNothing', () => {
  assert.equal(describeEvent({ method: 'Network.responseReceived', params: { response: { status: 200, url: 'http://localhost/' } } }), undefined);
});

test('describeEvent_ForARequestThatFailed_ReturnsIt_UnlessTheBrowserCanceledIt', () => {
  const failed = { method: 'Network.loadingFailed', params: { requestId: '7', errorText: 'net::ERR_CONNECTION_REFUSED', canceled: false } };
  const canceled = { method: 'Network.loadingFailed', params: { requestId: '8', errorText: 'net::ERR_ABORTED', canceled: true } };

  assert.match(describeEvent(failed), /request failed \(net::ERR_CONNECTION_REFUSED\)/);
  assert.equal(describeEvent(canceled), undefined);
});

test('createPageWatcher_WhenARequestNeverEnds_ReportsItAsWithoutAnAnswer', () => {
  const watcher = createPageWatcher();

  watcher.onEvent({ method: 'Network.requestWillBeSent', params: { requestId: '1', request: { url: 'http://localhost/stream' } } });
  watcher.onEvent({ method: 'Network.requestWillBeSent', params: { requestId: '2', request: { url: 'http://localhost/ok.json' } } });
  watcher.onEvent({ method: 'Network.loadingFinished', params: { requestId: '2' } });

  assert.deepEqual(watcher.unanswered(), ['no answer: http://localhost/stream']);
  assert.equal(watcher.quietFor(), 0);
});

test('createPageWatcher_IgnoresDataAndBlobUrls', () => {
  const watcher = createPageWatcher();

  watcher.onEvent({ method: 'Network.requestWillBeSent', params: { requestId: '1', request: { url: 'data:image/png;base64,AAAA' } } });
  watcher.onEvent({ method: 'Network.requestWillBeSent', params: { requestId: '2', request: { url: 'blob:http://localhost/1' } } });

  assert.deepEqual(watcher.unanswered(), []);
});

test('createPageWatcher_KeepsTheProblemsItSees', () => {
  const watcher = createPageWatcher();

  watcher.onEvent({ method: 'Network.responseReceived', params: { response: { status: 500, url: 'http://localhost/api' } } });

  assert.deepEqual(watcher.problems, ['HTTP 500: http://localhost/api']);
});

test('summarize_WhenEveryPageIsClean_SaysZeroErrorsInNOfN', () => {
  assert.equal(summarize([{ problems: [] }, { problems: [] }], 2), '0 errors in 2 of 2');
});

test('summarize_WhenAPageHasAProblem_DoesNotSayZero', () => {
  assert.equal(summarize([{ problems: [] }, { problems: ['HTTP 404: x'] }], 2), 'errors found: only 1 of 2 are clean');
});

test('summarize_WhenFewerPagesWereVisitedThanTheManifestLists_DoesNotSayZero', () => {
  assert.equal(summarize([{ problems: [] }], 2), 'errors found: only 1 of 2 are clean');
});
