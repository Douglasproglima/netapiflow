import assert from 'node:assert/strict';
import { test } from 'node:test';
import { announcedSections, checkAgainstExpected, entriesMissingFromTheChangelog, findDifferences } from './compare-with-published.mjs';

const CHANGELOG = `# Changelog

## [Unreleased]

### Changed

- **\`NetApiFlow\`: a request that matches no endpoint is now recorded only when it ends.** A static
  file used to be listed as running.

## [0.5.3] - 2026-11-20

### Fixed

- An older entry that is no longer news.
`;

const RELEASE_CHANGELOG = `# Changelog

## [Unreleased]

## [0.6.0] - 2026-11-25

### Changed

- **\`NetApiFlow\`: a request that matches no endpoint is now recorded only when it ends.** A static
  file used to be listed as running.

## [0.5.3] - 2026-11-20

### Fixed

- An older entry that is no longer news.

## [0.5.2] - 2026-11-19

### Fixed

- An entry from two releases ago.
`;

const HOTFIX_AND_RELEASE_CHANGELOG = `# Changelog

## [Unreleased]

## [0.6.0] - 2026-11-25

### Changed

- The entry the minor release announces.

## [0.5.4] - 2026-11-22

### Fixed

- The entry the hotfix announces.

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

test('announcedSections_StopsAtTheLastPublishedVersion', () => {
  const section = announcedSections(CHANGELOG, '0.5.3');

  assert.match(section, /a request that matches no endpoint is now recorded only when it ends/);
  assert.doesNotMatch(section, /older entry/);
});

test('announcedSections_WithoutTheUnreleasedSection_Throws', () => {
  assert.throws(() => announcedSections('# Changelog\n\n## [0.5.3] - 2026-11-20\n', '0.5.3'), /no "## \[Unreleased\]" section/);
});

test('entriesMissingFromTheChangelog_FindsTheTextAcrossALineBreak', () => {
  const wrapped = { ...LINE, changelog: 'A static file used to be listed as running.' };

  assert.deepEqual(entriesMissingFromTheChangelog([LINE, wrapped], CHANGELOG, '0.5.3'), []);
});

test('entriesMissingFromTheChangelog_WhenTheEntryIsOnlyInAnOlderRelease_IsMissing', () => {
  const old = { ...LINE, changelog: 'An older entry that is no longer news.' };

  assert.deepEqual(entriesMissingFromTheChangelog([old], CHANGELOG, '0.5.3'), [old]);
});

test('entriesMissingFromTheChangelog_WhenNothingAnnouncesTheLine_IsMissing', () => {
  const unannounced = { ...LINE, changelog: 'the trace id header moves to Production' };

  assert.deepEqual(entriesMissingFromTheChangelog([unannounced], CHANGELOG, '0.5.3'), [unannounced]);
});

test('entriesMissingFromTheChangelog_WhenTheReleaseMovedTheEntryAboveTheLastPublishedVersion_FindsIt', () => {
  assert.deepEqual(entriesMissingFromTheChangelog([LINE], RELEASE_CHANGELOG, '0.5.3'), []);
});

test('entriesMissingFromTheChangelog_WhenTheEntryIsInTheLastPublishedVersion_IsMissing', () => {
  const old = { ...LINE, changelog: 'An older entry that is no longer news.' };

  assert.deepEqual(entriesMissingFromTheChangelog([old], RELEASE_CHANGELOG, '0.5.3'), [old]);
});

test('entriesMissingFromTheChangelog_WhenTheEntryIsBelowTheLastPublishedVersion_IsMissing', () => {
  const older = { ...LINE, changelog: 'An entry from two releases ago.' };

  assert.deepEqual(entriesMissingFromTheChangelog([older], RELEASE_CHANGELOG, '0.5.3'), [older]);
});

test('entriesMissingFromTheChangelog_WhenTheEntryIsStillUnreleased_FindsIt', () => {
  assert.deepEqual(entriesMissingFromTheChangelog([LINE], CHANGELOG, '0.5.3'), []);
});

test('entriesMissingFromTheChangelog_WhenAHotfixAndAReleaseAreAboveTheLastPublishedVersion_FindsTheEntriesOfBoth', () => {
  const hotfix = { ...LINE, changelog: 'The entry the hotfix announces.' };
  const minor = { ...LINE, changelog: 'The entry the minor release announces.' };

  assert.deepEqual(entriesMissingFromTheChangelog([hotfix, minor], HOTFIX_AND_RELEASE_CHANGELOG, '0.5.3'), []);
});

test('entriesMissingFromTheChangelog_WithoutTheSectionOfTheLastPublishedVersion_Throws', () => {
  assert.throws(() => entriesMissingFromTheChangelog([LINE], CHANGELOG, '0.4.0'), /no "## \[0\.4\.0\]" section/);
});
