import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('..', import.meta.url));
const { renderDesignMd } = createRequire(import.meta.url)(
  `${repoRoot}src/NetApiFlow.Dashboard/ClientApp/scripts/generate-design-md.js`,
);

const tokens = readFileSync(`${repoRoot}docs/brand/tokens.md`, 'utf8');
const typographyHeading = '### Escala aplicada ao dashboard';

const withoutTypographyRow = (markdown, role) =>
  markdown
    .split('\n')
    .filter((line) => !line.startsWith(`| ${role} |`))
    .join('\n');

test('the committed DESIGN.md is what the generator writes from docs/brand/tokens.md', () => {
  const committed = readFileSync(`${repoRoot}DESIGN.md`, 'utf8');

  assert.equal(renderDesignMd(tokens), committed);
});

test('the typography scale is read from the scale section of tokens.md, seven roles in order', () => {
  const rendered = renderDesignMd(tokens);

  const typographyRows = rendered
    .split('\n')
    .slice(rendered.split('\n').findIndex((line) => line.startsWith('| Role | Family')) + 1)
    .filter((line) => line.startsWith('|') && !line.startsWith('|---'))
    .slice(0, 7);

  assert.equal(typographyRows.length, 7);
  assert.match(typographyRows[0], /^\| Page title \(topbar\) \| Plus Jakarta Sans \| 18 \/ 24 \| 700 \|$/);
  assert.match(typographyRows[6], /^\| HTTP verb badge \| JetBrains Mono \| 11 \/ 16, letter-spacing \.04em \| 600 \|$/);
});

test('a tokens.md without the scale section fails and names the section', () => {
  const withoutSection = tokens.replace(typographyHeading, '### Outra seção');

  assert.throws(() => renderDesignMd(withoutSection), /Section not found.*Escala aplicada ao dashboard/);
});

test('a scale with a missing role fails and says it read tokens.md', () => {
  const sixRoles = withoutTypographyRow(tokens, 'Badge de verbo');

  assert.throws(() => renderDesignMd(sixRoles), /Expected 7 typography rows in tokens\.md, found 6/);
});
