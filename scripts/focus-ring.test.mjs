import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CHECKS, CHECK_IDS, MINIMUM_CONTRAST, contrastRatio, readStyleSources, ruleProblems, themesOf, tokenProblems } from './focus-ring.mjs';

const DARK = `--naf-bg: #090b10; --naf-surface: #131722; --naf-surface-2: #1a2030; --naf-focus: #8b5cf6;`;
const LIGHT = `--naf-bg: #ffffff; --naf-surface: #f1f5f9; --naf-surface-2: #e2e8f0; --naf-focus: #6d28d9;`;

function tokens({ dark = DARK, light = LIGHT } = {}) {
  return `:root {
  ${dark}
  @media (prefers-color-scheme: light) {
    &:not([data-theme='dark']) {
      ${light}
    }
  }
}
:root[data-theme='light'] {
  ${light}
}
:root[data-theme='dark'] {
  ${dark}
}`;
}

const STYLES = `html { --mat-menu-item-focus-state-layer-color: transparent; }
.mat-mdc-button-base:focus-visible {
  outline: 2px solid var(--naf-focus);
  outline-offset: 2px;
}
.mat-mdc-menu-item.mat-focus-indicator:focus-visible {
  outline: 2px solid var(--naf-focus);
  outline-offset: -2px;
}`;

test('white on black is 21:1 and the ratio does not depend on the order of the colors', () => {
  assert.equal(Math.round(contrastRatio('#ffffff', '#000000')), 21);
  assert.equal(contrastRatio('#000000', '#ffffff'), contrastRatio('#ffffff', '#000000'));
});

test('the four blocks of tokens are found, each with its own focus color', () => {
  const themes = themesOf(tokens());

  assert.deepEqual([...themes.keys()], ['dark theme', 'light theme', 'dark theme without a saved choice', 'light theme without a saved choice']);
  assert.equal(themes.get('dark theme').get('--naf-focus'), '#8b5cf6');
  assert.equal(themes.get('dark theme without a saved choice').get('--naf-focus'), '#8b5cf6');
  assert.equal(themes.get('light theme without a saved choice').get('--naf-focus'), '#6d28d9');
});

test('a focus color that reaches 3:1 against the three surfaces of both themes passes', () => {
  assert.deepEqual(tokenProblems(tokens()), []);
});

test('a focus color below 3:1 names the theme, the surface and the ratio', () => {
  const problems = tokenProblems(tokens({ dark: DARK.replace('#8b5cf6', '#2e3850') }));

  assert.ok(problems.length > 0);
  assert.match(problems[0], /dark theme: the focus ring #2e3850 is \d\.\d\d:1 against --naf-bg #090b10, below 3:1\./);
});

test('a theme that does not declare the focus token is reported', () => {
  const problems = tokenProblems(tokens({ light: LIGHT.replace('--naf-focus: #6d28d9;', '') }));

  assert.ok(problems.some((problem) => problem.includes('light theme: --naf-focus is not declared')));
});

test('a block of tokens that is missing is reported', () => {
  assert.ok(tokenProblems(':root { --naf-bg: #000000; }').some((problem) => problem.includes('the block of tokens was not found')));
});

test('the rules of the Material buttons and of the menu items are accepted', () => {
  assert.deepEqual(ruleProblems(STYLES), []);
});

test('a missing ring on the Material buttons is reported', () => {
  const problems = ruleProblems(STYLES.replace('.mat-mdc-button-base:focus-visible', '.not-a-button:focus-visible'));

  assert.equal(problems.length, 1);
  assert.match(problems[0], /\.mat-mdc-button-base:focus-visible/);
});

test('a ring on the menu item without the extra class is reported, because Material outline: 0 wins', () => {
  const problems = ruleProblems(STYLES.replace('.mat-mdc-menu-item.mat-focus-indicator:focus-visible', '.mat-mdc-menu-item:focus-visible'));

  assert.equal(problems.length, 1);
  assert.match(problems[0], /mat-focus-indicator/);
});

test('a menu item that keeps its focus tint is reported', () => {
  const problems = ruleProblems(STYLES.replace('transparent', 'red'));

  assert.equal(problems.length, 1);
  assert.match(problems[0], /below 3:1/);
});

test('the tokens and the styles of the dashboard pass both checks', async () => {
  const { tokensScss, stylesScss } = await readStyleSources();

  assert.deepEqual(tokenProblems(tokensScss), []);
  assert.deepEqual(ruleProblems(stylesScss), []);
});

test('each check has an id of its own, for the regression guards', () => {
  assert.equal(MINIMUM_CONTRAST, 3);
  assert.deepEqual(CHECK_IDS, [CHECKS.tokens, CHECKS.rules]);
  assert.equal(new Set(CHECK_IDS).size, CHECK_IDS.length);
});
