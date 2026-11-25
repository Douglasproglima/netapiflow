import { readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const REPOSITORY_ROOT = fileURLToPath(new URL('..', import.meta.url));
const STYLES_DIRECTORY = 'src/NetApiFlow.Dashboard/ClientApp/src';
export const MINIMUM_CONTRAST = 3;
const SURFACE_TOKENS = ['--naf-bg', '--naf-surface', '--naf-surface-2'];
const FOCUS_TOKEN = '--naf-focus';
export const CHECKS = {
  tokens: 'focus-ring:tokens-reach-3-to-1',
  rules: 'focus-ring:material-buttons-and-menu-items',
};
export const CHECK_IDS = Object.values(CHECKS);

const THEME_OPENINGS = {
  'dark theme': ":root[data-theme='dark']",
  'light theme': ":root[data-theme='light']",
  'dark theme without a saved choice': ':root {',
  'light theme without a saved choice': "&:not([data-theme='dark'])",
};

const MATERIAL_BUTTON_RING = /\.mat-mdc-button-base:focus-visible\s*\{[^}]*outline:\s*2px solid var\(--naf-focus\)/;
const MATERIAL_MENU_ITEM_RING = /\.mat-mdc-menu-item\.mat-focus-indicator:focus-visible\s*\{[^}]*outline:\s*2px solid var\(--naf-focus\)/;
const MENU_ITEM_WITHOUT_TINT = /--mat-menu-item-focus-state-layer-color:\s*transparent/;

export function blockAfter(source, opening) {
  const start = source.indexOf(opening);
  if (start === -1) {
    return undefined;
  }

  const bodyStart = source.indexOf('{', start);
  let depth = 0;
  for (let index = bodyStart; index < source.length; index++) {
    if (source[index] === '{') {
      depth++;
    } else if (source[index] === '}' && --depth === 0) {
      return source.slice(bodyStart + 1, index);
    }
  }

  return undefined;
}

function declarationsOf(block) {
  const declarations = new Map();
  for (const [, name, value] of block.matchAll(/(--naf-[\w-]+):\s*(#[0-9a-fA-F]{6})\s*;/g)) {
    declarations.set(name, value);
  }

  return declarations;
}

function withoutNestedAtRules(block) {
  const nested = block.indexOf('@media');
  return nested === -1 ? block : block.slice(0, nested);
}

export function themesOf(tokensScss) {
  const themes = new Map();
  for (const [name, opening] of Object.entries(THEME_OPENINGS)) {
    const block = blockAfter(tokensScss, opening);
    themes.set(name, block === undefined ? undefined : declarationsOf(withoutNestedAtRules(block)));
  }

  return themes;
}

function channel(hex, offset) {
  const value = parseInt(hex.slice(1 + offset * 2, 3 + offset * 2), 16) / 255;
  return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
}

function luminance(hex) {
  return 0.2126 * channel(hex, 0) + 0.7152 * channel(hex, 1) + 0.0722 * channel(hex, 2);
}

export function contrastRatio(first, second) {
  const [lighter, darker] = [luminance(first), luminance(second)].sort((a, b) => b - a);
  return (lighter + 0.05) / (darker + 0.05);
}

export function tokenProblems(tokensScss) {
  const problems = [];
  for (const [theme, declarations] of themesOf(tokensScss)) {
    if (declarations === undefined) {
      problems.push(`${theme}: the block of tokens was not found.`);
      continue;
    }

    const focus = declarations.get(FOCUS_TOKEN);
    if (focus === undefined) {
      problems.push(`${theme}: ${FOCUS_TOKEN} is not declared.`);
      continue;
    }

    for (const surfaceToken of SURFACE_TOKENS) {
      const surface = declarations.get(surfaceToken);
      if (surface === undefined) {
        problems.push(`${theme}: ${surfaceToken} is not declared, so the focus ring cannot be measured against it.`);
        continue;
      }

      const ratio = contrastRatio(focus, surface);
      if (ratio < MINIMUM_CONTRAST) {
        problems.push(`${theme}: the focus ring ${focus} is ${ratio.toFixed(2)}:1 against ${surfaceToken} ${surface}, below ${MINIMUM_CONTRAST}:1.`);
      }
    }
  }

  return problems;
}

export function ruleProblems(stylesScss) {
  const problems = [];
  if (!MATERIAL_BUTTON_RING.test(stylesScss)) {
    problems.push('No rule draws a 2px var(--naf-focus) outline on .mat-mdc-button-base:focus-visible, so the Material buttons only show their faint state layer.');
  }

  if (!MATERIAL_MENU_ITEM_RING.test(stylesScss)) {
    problems.push('No rule draws a 2px var(--naf-focus) outline on .mat-mdc-menu-item.mat-focus-indicator:focus-visible. The extra class is what beats the outline: 0 that Material sets on .mat-mdc-menu-item:focus.');
  }

  if (!MENU_ITEM_WITHOUT_TINT.test(stylesScss)) {
    problems.push('--mat-menu-item-focus-state-layer-color is not transparent: the tint behind the ring of a menu item brings it below 3:1 in the dark theme.');
  }

  return problems;
}

export async function readStyleSources(root = REPOSITORY_ROOT) {
  return {
    tokensScss: await readFile(join(root, STYLES_DIRECTORY, 'styles/_tokens.scss'), 'utf8'),
    stylesScss: await readFile(join(root, STYLES_DIRECTORY, 'styles.scss'), 'utf8'),
  };
}
