import assert from 'node:assert/strict';
import { test } from 'node:test';
import { EXCEPTIONS, findCitations, report } from './check-citations.mjs';

const filesWith = (contents) => ({
  files: Object.keys(contents),
  readText: (file) => contents[file],
});

const citationsIn = (contents) => {
  const { files, readText } = filesWith(contents);
  return findCitations(files, readText);
};

test('a line that cites an ADR is reported with its file and its line', () => {
  const findings = citationsIn({ 'src/A.cs': 'class A\n{\n    /// <summary>Correlates logs (ADR-0017).</summary>\n}' });

  assert.deepEqual(findings.map((finding) => [finding.file, finding.line]), [['src/A.cs', 3]]);
});

test('each form of citation is reported', () => {
  const forms = [
    '// Default language (D-35).',
    '/// limits of risco R-10.',
    '/// the glob (see D-08 in docs/plano/decisoes.md).',
    '/** Six states of seção 7.2 do prompt. */',
    '// frozen in docs/adr/0007.md',
  ];

  for (const form of forms) {
    assert.equal(citationsIn({ 'src/A.cs': form }).length, 1, form);
  }
});

test('text that only looks like a citation is not reported', () => {
  const lines = [
    'const quarterlyReport = 1;',
    'var D5 = 5; // D-5 has one digit',
    'the word ADR alone, or an R-1 with one digit',
    'hash sha512-AbCdEfGh+IjKl/MnOp',
  ];

  assert.deepEqual(citationsIn({ 'src/A.cs': lines.join('\n') }), []);
});

test('the check passes once the planted citation is removed', () => {
  const planted = citationsIn({ 'src/NetApiFlow/Flow.cs': '// kept for ADR-0099' });
  const removed = citationsIn({ 'src/NetApiFlow/Flow.cs': '// kept for compatibility' });

  assert.equal(planted.length, 1);
  assert.equal(removed.length, 0);
});

test('a file listed in the exceptions is not reported', () => {
  const [exception] = [...EXCEPTIONS.keys()];

  assert.deepEqual(citationsIn({ [exception]: 'Derived from tokens.md (ADR-0009)' }), []);
});

test('every exception says why', () => {
  for (const [file, reason] of EXCEPTIONS) {
    assert.ok(reason.length > 20, `${file} has no reason`);
  }
});

test('a lock file and a binary file are not read', () => {
  const findings = citationsIn({ 'src/ClientApp/package-lock.json': 'ADR-0001', 'src/logo.png': 'ADR-0001' });

  assert.deepEqual(findings, []);
});

test('the report names every finding and says how to fix it', () => {
  const message = report(citationsIn({ 'src/A.ts': '// (D-35)' }));

  assert.match(message, /src\/A\.ts:1: \/\/ \(D-35\)/);
  assert.match(message, /EXCEPTIONS/);
});

test('the report of a clean tree says so', () => {
  assert.match(report([]), /No citation/);
});
