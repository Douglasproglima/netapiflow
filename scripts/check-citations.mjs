import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { extname } from 'node:path';
import { pathToFileURL } from 'node:url';

export const CITATION_PATTERN = /ADR-[0-9]|\bD-[0-9]{2,3}\b|\bR-[0-9]{2}\b|do prompt|docs\/plano|docs\/adr/;

const CHECKED_EXTENSIONS = new Set([
  '.cs', '.csproj', '.props', '.targets', '.ts', '.html', '.scss', '.css', '.js', '.mjs', '.json', '.md', '.xml', '.yml',
]);

const GENERATED_FILES = new Set(['package-lock.json']);

const CHECKED_FOLDERS = ['src', 'tests'];

export const EXCLUDED_FOLDERS = ['tests/manual-verification/', 'tests/regression-guards/'];

export const EXCEPTIONS = new Map([
  [
    'src/NetApiFlow.Dashboard/ClientApp/scripts/generate-design-md.js',
    'Text of the generated DESIGN.md, which names the ADRs that allow each exception to the design checks.',
  ],
  [
    'src/NetApiFlow.Dashboard/ClientApp/.impeccable/config.json',
    'The reason field of an exception to a design check names the ADRs that allow it.',
  ],
  [
    'src/NetApiFlow.Dashboard/ClientApp/scripts/README.md',
    'Describes the design tooling, which is defined by those ADRs.',
  ],
]);

export function isChecked(file) {
  const name = file.split('/').pop();
  return CHECKED_EXTENSIONS.has(extname(file))
    && !GENERATED_FILES.has(name)
    && !EXCEPTIONS.has(file)
    && !EXCLUDED_FOLDERS.some((folder) => file.startsWith(folder));
}

export function findCitations(files, readText) {
  const findings = [];
  for (const file of files.filter(isChecked)) {
    readText(file).split('\n').forEach((text, index) => {
      if (CITATION_PATTERN.test(text)) {
        findings.push({ file, line: index + 1, text: text.trim() });
      }
    });
  }
  return findings;
}

export function listTrackedFiles(root) {
  const output = execFileSync('git', ['ls-files', '-z', '--', ...CHECKED_FOLDERS], { cwd: root, encoding: 'utf8' });
  return output.split('\0').filter(Boolean);
}

export function report(findings) {
  if (findings.length === 0) {
    return 'No citation of an ADR, a decision, a risk or a plan document in src/ or tests/.';
  }
  const lines = findings.map((finding) => `${finding.file}:${finding.line}: ${finding.text}`);
  return [
    `${findings.length} line(s) of src/ or tests/ cite an ADR, a decision, a risk or a plan document:`,
    ...lines,
    'Say the reason in words and drop the number. An exception goes in EXCEPTIONS of scripts/check-citations.mjs, one line per file, with the reason.',
  ].join('\n');
}

function main() {
  const root = process.cwd();
  const findings = findCitations(listTrackedFiles(root), (file) => readFileSync(`${root}/${file}`, 'utf8'));
  console.log(report(findings));
  process.exitCode = findings.length === 0 ? 0 : 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main();
}
