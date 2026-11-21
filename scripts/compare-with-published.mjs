import { readFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { clientFor, delay, freePort, startApplication, untilReady } from './app-harness.mjs';

const TRACE_ID_HEADER = 'x-netapiflow-traceid';
const STORED_TIMEOUT_MS = 15_000;
const CONTROL_ROUTE = '/control';
const DASHBOARD_PREFIX = '/flow';
const COMPARED_SIDES = ['old', 'new'];

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

export const VARIANTS = {
  default: { environment: 'Development', pathBase: '' },
  'exclude-internal': { environment: 'Development', pathBase: '' },
  'exclude-error': { environment: 'Development', pathBase: '' },
  'handler-exclude-internal': { environment: 'Development', pathBase: '' },
  'handler-exclude-error': { environment: 'Development', pathBase: '' },
  include: { environment: 'Development', pathBase: '' },
  pathbase: { environment: 'Development', pathBase: '/api' },
  production: { environment: 'Production', pathBase: '', appVariant: 'default', dashboard: false },
};

export const SCENARIOS = [
  { id: 'status-page-reexecution-excluded-path', variant: 'exclude-internal', requests: ['/internal/missing'] },
  { id: 'status-page-reexecution-recorded-path', variant: 'exclude-internal', requests: ['/missing'] },
  { id: 'status-page-reexecution-excluded-error-path', variant: 'exclude-error', requests: ['/missing'] },
  { id: 'exception-handler-reexecution-excluded-path', variant: 'handler-exclude-internal', requests: ['/internal/crash'] },
  { id: 'exception-handler-reexecution-recorded-path', variant: 'handler-exclude-internal', requests: ['/crash'] },
  { id: 'exception-handler-reexecution-excluded-error-path', variant: 'handler-exclude-error', requests: ['/crash'] },
  { id: 'exclude-paths-on-a-matched-endpoint', variant: 'exclude-internal', requests: ['/internal/ping', '/orders/1'] },
  { id: 'include-paths-on-a-matched-endpoint', variant: 'include', requests: ['/orders/1', '/health', '/boom'] },
  { id: 'static-file-and-request-without-a-route', variant: 'default', requests: ['/site.css', '/nothing-matches-this'] },
  { id: 'controller-and-minimal-api-endpoint', variant: 'default', requests: ['/items/1', '/orders/1'] },
  { id: 'endpoint-that-throws-with-nothing-handling-it', variant: 'default', requests: ['/boom'] },
  { id: 'exclude-paths-under-a-path-base', variant: 'pathbase', requests: ['/health', '/orders/1'] },
  { id: 'trace-id-header-in-production', variant: 'production', requests: ['/orders/1'] },
  { id: 'slow-request-in-a-terminal-middleware', variant: 'default', hold: '/terminal' },
  {
    id: 'requests-that-share-a-traceparent',
    variant: 'default',
    requests: ['/orders/1', '/items/1', '/orders/2'],
    headers: { traceparent: `00-${'a'.repeat(32)}-${'b'.repeat(16)}-01` },
  },
];

function hasTraceIdHeader(response) {
  return response.headers[TRACE_ID_HEADER] !== undefined;
}

function isTraceOfTheApp(trace) {
  return trace.routeTemplate !== CONTROL_ROUTE && !trace.routeTemplate.startsWith(DASHBOARD_PREFIX);
}

function describeTrace(trace) {
  return `${trace.method} ${trace.routeTemplate} ${trace.httpStatus ?? trace.status}`;
}

function siteFor(baseUrl, variant) {
  const client = clientFor(baseUrl);
  return {
    variant,
    get: (path, headers) => client.get(variant.pathBase + path, headers),
    send: (options) => client.send({ ...options, path: variant.pathBase + options.path }),
  };
}

async function untilStored(site, traceId) {
  const deadline = Date.now() + STORED_TIMEOUT_MS;
  while (Date.now() < deadline) {
    if ((await site.get(`${DASHBOARD_PREFIX}/api/traces/${traceId}`)).status === 200) {
      return;
    }

    await delay(100);
  }

  throw new Error(`The trace ${traceId} was not stored in ${STORED_TIMEOUT_MS} ms.`);
}

async function untilEverythingBeforeIsStored(site) {
  const control = await site.get(CONTROL_ROUTE);
  const traceId = control.headers[TRACE_ID_HEADER];
  if (traceId === undefined) {
    throw new Error(`${CONTROL_ROUTE} answered without ${TRACE_ID_HEADER}, so the end of the earlier writes cannot be told.`);
  }

  await untilStored(site, traceId);
}

async function clearTraces(site) {
  await untilEverythingBeforeIsStored(site);
  const cleared = await site.send({ method: 'DELETE', path: `${DASHBOARD_PREFIX}/api/traces` });
  if (cleared.status !== 200 && cleared.status !== 204) {
    throw new Error(`DELETE ${DASHBOARD_PREFIX}/api/traces answered ${cleared.status}.`);
  }
}

async function recordedTraces(site) {
  await untilEverythingBeforeIsStored(site);
  const listing = await site.get(`${DASHBOARD_PREFIX}/api/traces?pageSize=200`);
  return JSON.parse(listing.text).items.filter(isTraceOfTheApp).map(describeTrace).sort();
}

async function liveTraces(site) {
  const listing = JSON.parse((await site.get(`${DASHBOARD_PREFIX}/api/traces/live`)).text);
  const items = (Array.isArray(listing) ? listing : listing.items).filter(isTraceOfTheApp);
  const described = await Promise.all(
    items.map(async (trace) => {
      const detail = await site.get(`${DASHBOARD_PREFIX}/api/traces/${trace.traceId}`);
      const steps = detail.status === 200 ? `steps=${JSON.parse(detail.text).spans.length}` : `detail=${detail.status}`;
      return `${describeTrace(trace)} ${steps}`;
    }),
  );
  return described.sort();
}

function observeResponse(observation, path, response) {
  observation[`GET ${path} status`] = response.status;
  observation[`GET ${path} header`] = hasTraceIdHeader(response);
}

async function observeRequests(site, scenario) {
  const observation = {};
  if (site.variant.dashboard !== false) {
    await clearTraces(site);
  }

  for (const path of scenario.requests) {
    observeResponse(observation, path, await site.get(path, scenario.headers));
  }

  if (site.variant.dashboard !== false) {
    observation['traces after'] = await recordedTraces(site);
  }

  return observation;
}

async function observeHeldRequest(site, scenario) {
  const observation = {};
  await clearTraces(site);
  await site.get('/__probe/reset/terminal');
  const held = site.get(scenario.hold);
  let response;
  try {
    await site.get('/__probe/inside/terminal');
    observation['traces during'] = await liveTraces(site);
  } finally {
    await site.get('/__probe/release/terminal');
    response = await held;
  }

  observeResponse(observation, scenario.hold, response);
  observation['traces after'] = await recordedTraces(site);
  return observation;
}

export async function observe(site, scenario) {
  return scenario.hold ? observeHeldRequest(site, scenario) : observeRequests(site, scenario);
}

export function findDifferences(scenarioId, observations) {
  const aspects = [...new Set(COMPARED_SIDES.flatMap((side) => Object.keys(observations[side])))];
  return aspects
    .filter((aspect) => JSON.stringify(observations.old[aspect]) !== JSON.stringify(observations.new[aspect]))
    .map((aspect) => ({ scenario: scenarioId, aspect, old: observations.old[aspect] ?? null, new: observations.new[aspect] ?? null }));
}

function sameDifference(left, right) {
  return left.scenario === right.scenario
    && left.aspect === right.aspect
    && JSON.stringify(left.old) === JSON.stringify(right.old)
    && JSON.stringify(left.new) === JSON.stringify(right.new);
}

export function checkAgainstExpected(found, expected) {
  return {
    unexpected: found.filter((difference) => !expected.some((line) => sameDifference(line, difference))),
    missing: expected.filter((line) => !found.some((difference) => sameDifference(line, difference))),
  };
}

function normalizeSpaces(text) {
  return text.replace(/\s+/g, ' ');
}

export function unreleasedSection(changelog) {
  const start = changelog.search(/^## \[Unreleased\]/m);
  if (start < 0) {
    throw new Error('The CHANGELOG has no "## [Unreleased]" section.');
  }

  const rest = changelog.slice(start + 1);
  const next = rest.search(/^## \[/m);
  return normalizeSpaces(next < 0 ? rest : rest.slice(0, next));
}

export function entriesMissingFromTheChangelog(expected, changelog) {
  const unreleased = unreleasedSection(changelog);
  return expected.filter((line) => !unreleased.includes(normalizeSpaces(line.changelog)));
}

function formatDifference(difference) {
  return `${difference.scenario}: ${difference.aspect}: the published package gives ${JSON.stringify(difference.old)}, this build gives ${JSON.stringify(difference.new)}`;
}

async function compareVariant(name, dlls, scenarios) {
  const variant = VARIANTS[name];
  const applications = {};
  const sites = {};
  try {
    for (const side of COMPARED_SIDES) {
      const port = await freePort();
      applications[side] = startApplication(dlls[side], { environment: variant.environment, variables: { COMPARISON_VARIANT: variant.appVariant ?? name } }, port);
      sites[side] = siteFor(`http://127.0.0.1:${port}`, variant);
    }

    for (const side of COMPARED_SIDES) {
      await untilReady(sites[side], applications[side], '/hello');
    }

    const found = [];
    for (const scenario of scenarios) {
      const observations = {};
      for (const side of COMPARED_SIDES) {
        observations[side] = await observe(sites[side], scenario);
      }

      const differences = findDifferences(scenario.id, observations);
      console.log(`${differences.length === 0 ? 'SAME' : 'DIFF'} ${scenario.id} [${name}]`);
      found.push(...differences);
    }

    return found;
  } catch (error) {
    for (const side of COMPARED_SIDES) {
      console.error(`\nLast lines of the log of the ${side} app (${name}):\n${applications[side]?.logTail() ?? ''}`);
    }

    throw error;
  } finally {
    await Promise.all(Object.values(applications).map((application) => application.stop()));
  }
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const expected = JSON.parse(await readFile(options.expected, 'utf8'));
  const changelog = await readFile(options.changelog, 'utf8');

  const found = [];
  for (const name of options.variants) {
    found.push(...(await compareVariant(name, { old: options.oldDll, new: options.newDll }, SCENARIOS.filter((scenario) => scenario.variant === name))));
  }

  if (options.print) {
    console.log(JSON.stringify(found, null, 2));
    return;
  }

  const { unexpected, missing } = checkAgainstExpected(found, expected);
  const unannounced = entriesMissingFromTheChangelog(expected, changelog);

  console.log(`\nSUMMARY scenarios=${SCENARIOS.filter((scenario) => options.variants.includes(scenario.variant)).length} differences=${found.length} expected=${expected.length}`);
  for (const difference of unexpected) {
    console.error(`UNEXPECTED ${formatDifference(difference)}`);
  }

  for (const line of missing) {
    console.error(`NO LONGER HAPPENS ${formatDifference(line)}. Remove its line from the list of expected differences.`);
  }

  for (const line of unannounced) {
    console.error(`NOT ANNOUNCED ${line.scenario}: ${line.aspect}: the CHANGELOG [Unreleased] has no "${line.changelog}"`);
  }

  if (unexpected.length + missing.length + unannounced.length > 0) {
    process.exit(1);
  }
}

function parseArguments(argumentList) {
  const options = {
    expected: join(repoRoot, 'tests/real-package/expected-differences.json'),
    changelog: join(repoRoot, 'CHANGELOG.md'),
    variants: Object.keys(VARIANTS),
    print: false,
  };
  for (let index = 0; index < argumentList.length; index++) {
    const name = argumentList[index];
    if (name === '--print') {
      options.print = true;
      continue;
    }

    const value = argumentList[++index];
    if (name === '--new-dll') {
      options.newDll = value;
    } else if (name === '--old-dll') {
      options.oldDll = value;
    } else if (name === '--expected') {
      options.expected = value;
    } else if (name === '--changelog') {
      options.changelog = value;
    } else if (name === '--variants') {
      options.variants = value.split(',');
    } else {
      throw new Error(`Unknown argument "${name}". Use --new-dll <dll> --old-dll <dll> [--expected <json>] [--changelog <md>] [--variants a,b] [--print].`);
    }
  }

  if (!options.newDll || !options.oldDll) {
    throw new Error('Pass --new-dll and --old-dll, the two builds of the same app.');
  }

  const unknown = options.variants.filter((name) => !(name in VARIANTS));
  if (unknown.length > 0) {
    throw new Error(`Unknown variant ${unknown.join(', ')}. The variants are: ${Object.keys(VARIANTS).join(', ')}.`);
  }

  return options;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  await main();
}
