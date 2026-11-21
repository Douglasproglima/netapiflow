import assert from 'node:assert/strict';
import { test } from 'node:test';
import { checkAgainstExpected, entriesMissingFromTheChangelog, findDifferences, unreleasedSection } from './compare-with-published.mjs';

const CHANGELOG = `# Changelog

## [Unreleased]

### Changed

- **\`NetApiFlow\`: a request that matches no endpoint is now recorded only when it ends.** A static
  file used to be listed as running.

## [0.5.3] - 2026-11-20

### Fixed

- An older entry that is no longer news.
`;

const LINE = {
  scenario: 'slow-request',
  aspect: 'traces during',
  old: ['GET (unmatched) Running steps=1'],
  new: [],
  reason: 'It is published when it ends.',
  changelog: 'a request that matches no endpoint is now recorded only when it ends.',
};

test('findDifferences_WhenBothSidesObserveTheSame_ReportsNothing', () => {
  const observation = { 'GET /a status': 200, 'traces after': ['GET /a 200'] };

  assert.deepEqual(findDifferences('same', { old: observation, new: { ...observation } }), []);
});

test('findDifferences_NamesTheScenarioTheAspectAndBothValues', () => {
  const found = findDifferences('health', {
    old: { 'GET /health header': true, 'GET /health status': 200 },
    new: { 'GET /health header': false, 'GET /health status': 200 },
  });

  assert.deepEqual(found, [{ scenario: 'health', aspect: 'GET /health header', old: true, new: false }]);
});

test('findDifferences_WhenOneSideLacksAnAspect_ReportsItWithNull', () => {
  const found = findDifferences('live', { old: { 'traces during': ['x'] }, new: {} });

  assert.deepEqual(found, [{ scenario: 'live', aspect: 'traces during', old: ['x'], new: null }]);
});

test('checkAgainstExpected_WhenEveryDifferenceIsListedAndEveryLineAppears_IsClean', () => {
  const result = checkAgainstExpected([{ scenario: LINE.scenario, aspect: LINE.aspect, old: LINE.old, new: LINE.new }], [LINE]);

  assert.deepEqual(result, { unexpected: [], missing: [] });
});

test('checkAgainstExpected_WhenADifferenceIsNotListed_IsUnexpected', () => {
  const found = [{ scenario: 'status-page', aspect: 'GET /x header', old: false, new: true }];

  const result = checkAgainstExpected(found, []);

  assert.deepEqual(result.unexpected, found);
  assert.deepEqual(result.missing, []);
});

test('checkAgainstExpected_WhenAListedLineNoLongerAppears_IsMissing', () => {
  const result = checkAgainstExpected([], [LINE]);

  assert.deepEqual(result.missing, [LINE]);
  assert.deepEqual(result.unexpected, []);
});

test('checkAgainstExpected_WhenTheValueChangedFromTheListedOne_IsBothUnexpectedAndMissing', () => {
  const found = [{ scenario: LINE.scenario, aspect: LINE.aspect, old: LINE.old, new: ['GET (unmatched) Running steps=0'] }];

  const result = checkAgainstExpected(found, [LINE]);

  assert.equal(result.unexpected.length, 1);
  assert.equal(result.missing.length, 1);
});

test('unreleasedSection_StopsAtTheNextRelease', () => {
  const section = unreleasedSection(CHANGELOG);

  assert.match(section, /a request that matches no endpoint is now recorded only when it ends/);
  assert.doesNotMatch(section, /older entry/);
});

test('unreleasedSection_WithoutTheSection_Throws', () => {
  assert.throws(() => unreleasedSection('# Changelog\n\n## [0.5.3] - 2026-11-20\n'), /no "## \[Unreleased\]" section/);
});

test('entriesMissingFromTheChangelog_FindsTheTextAcrossALineBreak', () => {
  const wrapped = { ...LINE, changelog: 'A static file used to be listed as running.' };

  assert.deepEqual(entriesMissingFromTheChangelog([LINE, wrapped], CHANGELOG), []);
});

test('entriesMissingFromTheChangelog_WhenTheEntryIsOnlyInAnOlderRelease_IsMissing', () => {
  const old = { ...LINE, changelog: 'An older entry that is no longer news.' };

  assert.deepEqual(entriesMissingFromTheChangelog([old], CHANGELOG), [old]);
});

test('entriesMissingFromTheChangelog_WhenNothingAnnouncesTheLine_IsMissing', () => {
  const unannounced = { ...LINE, changelog: 'the trace id header moves to Production' };

  assert.deepEqual(entriesMissingFromTheChangelog([unannounced], CHANGELOG), [unannounced]);
});
