// Records the dashboard's own REST answers from a running sample app into the fixture set the
// static demo build serves. Nothing here edits a payload by hand: whatever the API returns is
// what the demo shows, so the demo can never drift from the real contract.
//
//   node scripts/generate-demo-fixtures.mjs [--base-url http://localhost:5080] [--dashboard /flow]
//
// Start samples/SampleApi.Controllers first, then run this. It exercises the demo scenarios of
// Fase 26 (success, slow, error, sequential N+1) a few times so History, Insights and
// Architecture are not empty, then captures every route ApiService calls.

import { mkdir, rm, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const outputDirectory = join(
  repoRoot,
  'src/NetApiFlow.Dashboard/ClientApp/src/assets/demo-fixtures',
);

const options = parseArguments(process.argv.slice(2));
const apiRoot = `${options.baseUrl}${options.dashboardPath}/api`;

const SCENARIOS = [
  { path: '/api/orders/1', times: 6 },
  { path: '/api/orders/parallel', times: 3 },
  { path: '/api/orders/sequential', times: 3 },
  { path: '/api/orders/999', times: 2 },
];

const DELAY_HEADER = 'X-NetApiFlow-Demo-Delay-Ms';

await main();

async function main() {
  await warmUp();

  const manifest = { routes: {}, fallbacks: {}, documents: {} };

  await capture(manifest, 'api/config');
  await capture(manifest, 'api/endpoints');
  await capture(manifest, 'api/metrics');
  await capture(manifest, 'api/metrics/components');
  await capture(manifest, 'api/metrics/routes');
  await capture(manifest, 'api/traces');
  await capture(manifest, 'api/traces/live');

  const traces = await getJson('api/traces');
  const summaries = traces?.items ?? [];
  if (summaries.length === 0) {
    throw new Error('The sample app returned no traces. Is it running, with NetApiFlow mounted?');
  }

  for (const summary of summaries) {
    await capture(manifest, `api/traces/${summary.traceId}`);
  }

  // A span the demo has no recorded detail for falls back to the first trace's, which is better
  // than a 404 in a build where the user cannot produce new data.
  manifest.fallbacks['api/traces'] = manifest.routes[`api/traces/${summaries[0].traceId}`];

  const routes = new Map();
  for (const summary of summaries) {
    routes.set(`${summary.method} ${summary.routeTemplate}`, summary);
  }

  for (const summary of routes.values()) {
    // Decoded and sorted by name: the same canonical key the interceptor derives from the
    // dashboard's own request, whose escaping comes from HttpParams and differs from this one's.
    const key = `method=${summary.method}&route=${summary.routeTemplate}`;
    const query = `method=${encodeURIComponent(summary.method)}&route=${encodeURIComponent(summary.routeTemplate)}`;
    await capture(manifest, `api/architecture?${key}`, `api/architecture?${query}`);
    await capture(manifest, `api/blueprint?${key}`, `api/blueprint?${query}`);
  }

  await captureOpenApiDocument(manifest);

  await writeFile(
    join(outputDirectory, 'manifest.json'),
    `${JSON.stringify(manifest, null, 2)}\n`,
    'utf8',
  );

  console.log(`Wrote ${Object.keys(manifest.routes).length} fixtures to ${outputDirectory}`);
}

async function warmUp() {
  await rm(outputDirectory, { recursive: true, force: true });
  await mkdir(outputDirectory, { recursive: true });

  for (const scenario of SCENARIOS) {
    for (let attempt = 0; attempt < scenario.times; attempt++) {
      const delay = attempt % 2 === 0 ? '250' : '0';
      await fetch(`${options.baseUrl}${scenario.path}`, {
        headers: { [DELAY_HEADER]: delay },
      }).catch(() => undefined);
    }
  }

  // Persistence runs on a background queue, and the very first write also creates the schema, so
  // wait for the history to actually report the traces rather than guessing a delay.
  for (let attempt = 0; attempt < 30; attempt++) {
    await new Promise((resolve) => setTimeout(resolve, 1000));
    const traces = await getJson('api/traces');
    if ((traces?.items?.length ?? 0) > 0) {
      return;
    }
  }

  throw new Error('The sample app recorded no traces within 30s. Is it running, with NetApiFlow mounted?');
}

/**
 * The Runner reads the host's own OpenAPI document, which is not a dashboard route. Without it
 * recorded, the demo would log a 404 on every page load and the Runner would show its error state.
 */
async function captureOpenApiDocument(manifest) {
  const config = await getJson('api/config');
  const documentPath = config?.openApiDocumentPath;
  if (!documentPath) {
    return;
  }

  const response = await fetch(`${options.baseUrl}${documentPath}`).catch(() => undefined);
  if (!response?.ok) {
    console.warn(`Skipped ${documentPath}: the host did not serve an OpenAPI document.`);
    return;
  }

  const fileName = 'openapi-document.json';
  await writeFile(join(outputDirectory, fileName), await response.text(), 'utf8');
  manifest.documents[documentPath] = fileName;
}

async function capture(manifest, requestKey, fetchKey = requestKey) {
  const body = await getJson(fetchKey);
  if (body === undefined) {
    console.warn(`Skipped ${requestKey}: the API did not answer with JSON.`);
    return;
  }

  const fileName = `${requestKey.replace(/^api\//, '').replace(/[^a-zA-Z0-9.-]/g, '_')}.json`;
  await writeFile(join(outputDirectory, fileName), `${JSON.stringify(body, null, 2)}\n`, 'utf8');
  manifest.routes[requestKey] = fileName;
}

async function getJson(requestKey) {
  const url = `${apiRoot}/${requestKey.replace(/^api\//, '')}`;
  const response = await fetch(url);
  if (!response.ok) {
    return undefined;
  }

  const text = await response.text();
  return text.length === 0 ? null : JSON.parse(text);
}

function parseArguments(argv) {
  const parsed = { baseUrl: 'http://localhost:5080', dashboardPath: '/flow' };
  for (let i = 0; i < argv.length; i += 2) {
    if (argv[i] === '--base-url') {
      parsed.baseUrl = argv[i + 1].replace(/\/$/, '');
    } else if (argv[i] === '--dashboard') {
      parsed.dashboardPath = `/${argv[i + 1].replace(/^\/|\/$/g, '')}`;
    }
  }

  return parsed;
}
