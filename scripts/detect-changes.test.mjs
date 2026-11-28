import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const workflow = readFileSync(fileURLToPath(new URL('../.github/workflows/ci.yml', import.meta.url)), 'utf8');

const REAL_PACKAGE_INPUT_RULE = /grep -qE '([^']+)'; then\s+echo "(?:readme|contract_docs)_changed=true"/;

function filesThatTurnOnTheRealPackage() {
  const rule = REAL_PACKAGE_INPUT_RULE.exec(workflow);
  assert.ok(rule, 'detect-changes of ci.yml has no rule that turns the real-package job on for a changed document');
  return new RegExp(rule[1]);
}

test('the README of each language turns the real-package job on', () => {
  const turnsOn = filesThatTurnOnTheRealPackage();

  assert.ok(turnsOn.test('README.md'));
  assert.ok(turnsOn.test('README.pt-BR.md'));
});

test('the CHANGELOG turns the real-package job on, because the comparison reads it', () => {
  assert.ok(filesThatTurnOnTheRealPackage().test('CHANGELOG.md'));
});

test('a document that the real-package job does not read does not turn it on', () => {
  const turnsOn = filesThatTurnOnTheRealPackage();

  for (const file of ['docs/brand/README.md', 'src/NetApiFlow/README.md', 'docs/gitflow.md', 'CHANGELOG.md.bak', 'CONTRIBUTING.md']) {
    assert.ok(!turnsOn.test(file), file);
  }
});
