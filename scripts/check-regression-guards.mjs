import { existsSync } from 'node:fs';
import { readdir, readFile } from 'node:fs/promises';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const KINDS = ['dotnet', 'vitest', 'ci-scenario'];
const JOBS = ['build-test', 'real-package', 'comparison', 'built-demo'];
const REQUIRED_FIELDS = ['id', 'defect', 'fixedIn', 'guard', 'job'];
const PASSED_BY_KIND = { dotnet: 'Passed', vitest: 'passed' };

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

export function parseTrx(xml) {
  return [...xml.matchAll(/<UnitTestResult\b[^>]*>/g)].map(([element]) => ({
    name: decodeEntities(/\btestName="([^"]*)"/.exec(element)?.[1] ?? ''),
    outcome: /\boutcome="([^"]*)"/.exec(element)?.[1] ?? 'Unknown',
  }));
}

function decodeEntities(value) {
  return value
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>')
    .replaceAll('&quot;', '"')
    .replaceAll('&apos;', "'")
    .replaceAll('&amp;', '&');
}

export function parseVitest(report) {
  return report.testResults.flatMap((file) =>
    file.assertionResults.map((assertion) => ({
      file: file.name.replaceAll('\\', '/'),
      name: assertion.fullName,
      status: assertion.status,
    })),
  );
}

export function structuralProblems(guards) {
  const problems = [];
  const seen = new Set();

  for (const guard of guards) {
    const label = guard.id ?? JSON.stringify(guard).slice(0, 60);
    for (const field of REQUIRED_FIELDS) {
      if (guard[field] === undefined || guard[field] === '') {
        problems.push(`${label}: the field "${field}" is missing.`);
      }
    }

    if (guard.id !== undefined && seen.has(guard.id)) {
      problems.push(`${label}: the id appears twice.`);
    }

    seen.add(guard.id);

    if (guard.guard !== undefined && !KINDS.includes(guard.guard.kind)) {
      problems.push(`${label}: the kind "${guard.guard.kind}" is not one of ${KINDS.join(', ')}.`);
    }

    if (guard.job !== undefined && !JOBS.includes(guard.job)) {
      problems.push(`${label}: the job "${guard.job}" is not one of ${JOBS.join(', ')}.`);
    }
  }

  return problems;
}

function dotnetMatches(results, test) {
  return results.filter((result) => result.name === test || result.name.startsWith(`${test}(`));
}

function vitestMatches(results, file, test) {
  return results.filter((result) => result.file.endsWith(`/${file}`) && result.name.startsWith(test));
}

export function checkGuard(guard, sources) {
  const { kind } = guard.guard;

  if (kind === 'dotnet') {
    const trx = sources.dotnet.get(guard.guard.project);
    if (trx === undefined) {
      return `${guard.id}: the results of ${guard.guard.project} are not in the run, so the guard cannot be found.`;
    }

    return outcomeProblem(guard, dotnetMatches(trx, guard.guard.test), guard.guard.test, PASSED_BY_KIND.dotnet, (result) => result.outcome);
  }

  if (kind === 'vitest') {
    if (sources.vitest === undefined) {
      return undefined;
    }

    const found = vitestMatches(sources.vitest, guard.guard.file, guard.guard.test);
    return outcomeProblem(guard, found, `${guard.guard.file}: ${guard.guard.test}`, PASSED_BY_KIND.vitest, (result) => result.status);
  }

  const scenarios = sources.scenarios[guard.job];
  if (scenarios === undefined || !scenarios.has(guard.guard.scenario)) {
    return `${guard.id}: the job ${guard.job} has no scenario "${guard.guard.scenario}" (removed or renamed?).`;
  }

  return undefined;
}

function outcomeProblem(guard, found, description, passed, outcomeOf) {
  if (found.length === 0) {
    return `${guard.id}: no test "${description}" in the results (deleted or renamed?).`;
  }

  const notPassed = found.filter((result) => outcomeOf(result) !== passed);
  if (notPassed.length > 0) {
    return `${guard.id}: "${description}" is ${[...new Set(notPassed.map(outcomeOf))].join(', ')}, not ${passed} (a skipped test guards nothing).`;
  }

  return undefined;
}

export function checkRegistry(guards, sources) {
  const structural = structuralProblems(guards);
  if (structural.length > 0) {
    return structural;
  }

  return guards.map((guard) => checkGuard(guard, sources)).filter((problem) => problem !== undefined);
}

async function loadDotnet(directory) {
  const sources = new Map();
  for (const name of (await readdir(directory)).filter((file) => file.endsWith('.trx'))) {
    sources.set(basename(name, '.trx'), parseTrx(await readFile(join(directory, name), 'utf8')));
  }

  return sources;
}

async function loadScenarios() {
  const packageScenarios = await import('./package-scenarios.mjs');
  const comparison = await import('./compare-with-published.mjs');
  const userApp = await import('./build-user-app.mjs');
  const demo = await import('./verify-built-demo.mjs');
  const livePanel = await import('./verify-live-panel.mjs');
  const snapshotFiles = (await readdir(join(repoRoot, 'tests/nuspec-snapshot'))).filter((file) => file.endsWith('.txt'));

  return {
    'real-package': new Set([
      userApp.SCENARIO_ID,
      ...packageScenarios.SCENARIOS.map((scenario) => scenario.id),
      ...livePanel.CHECK_IDS,
      ...snapshotFiles.map((file) => `nuspec:${basename(file, '.txt')}`),
    ]),
    comparison: new Set(comparison.SCENARIOS.map((scenario) => scenario.id)),
    'built-demo': new Set(demo.CHECK_IDS),
  };
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const guards = JSON.parse(await readFile(options.guards, 'utf8'));

  const vitestResult = options.vitest && existsSync(options.vitest) ? parseVitest(JSON.parse(await readFile(options.vitest, 'utf8'))) : undefined;
  const sources = { dotnet: await loadDotnet(options.trx), vitest: vitestResult, scenarios: await loadScenarios() };

  const problems = checkRegistry(guards, sources);
  const vitestGuards = guards.filter((guard) => guard.guard?.kind === 'vitest').length;

  console.log(`${guards.length} regression guards, ${guards.length - problems.length} found and passing.`);
  if (vitestResult === undefined && vitestGuards > 0) {
    console.log(`${vitestGuards} of them are vitest guards, which were not checked: this run has no vitest report, because the dashboard did not change.`);
  }

  if (problems.length > 0) {
    console.error(`\n${problems.join('\n')}`);
    console.error('\nA guard that was deleted, renamed or skipped no longer protects what its row says. Restore it, or change its row in tests/regression-guards/guards.json and say why in the pull request.');
    process.exit(1);
  }
}

function parseArguments(argumentList) {
  const options = { guards: join(repoRoot, 'tests/regression-guards/guards.json') };
  for (let index = 0; index < argumentList.length; index += 2) {
    const name = argumentList[index];
    const value = argumentList[index + 1];
    if (name === '--guards' || name === '--trx' || name === '--vitest') {
      options[name.slice(2)] = value;
    } else {
      throw new Error(`Unknown argument "${name}". Use --trx <folder of .trx files> [--vitest <report.json>] [--guards <guards.json>].`);
    }
  }

  if (!options.trx) {
    throw new Error('Pass --trx, the folder with the .trx files of the dotnet test run.');
  }

  return options;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  await main();
}
