import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const clientAppDirectory = join(repoRoot, 'src/NetApiFlow.Dashboard/ClientApp');
const outputDirectory = join(clientAppDirectory, 'src/assets/demo-fixtures');

const options = parseArguments(process.argv.slice(2));
const apiRoot = `${options.baseUrl}${options.dashboardPath}/api`;

const SCENARIOS = [
  { path: '/api/orders/1', times: 6 },
  { path: '/api/orders/parallel', times: 3 },
  { path: '/api/orders/sequential', times: 3 },
  { path: '/api/orders/999', times: 2 },
  { path: '/api/orders/1/confirm', times: 2 },
  { path: '/api/orders/1/slow', times: 2 },
  { path: '/api/orders/1/shipping-status', times: 2 },
];

const DELAY_HEADER = 'X-NetApiFlow-Demo-Delay-Ms';
const TRACE_ID_HEADER = 'x-netapiflow-traceid';
const SAMPLE_PARAMETER_VALUE = '1';

let prettier;

await main();

async function main() {
  if (options.anchorBlueprintsOnly) {
    await anchorBlueprintsOfTheRecordedFixtures();
    return;
  }

  await warmUp();

  const manifest = { routes: {}, fallbacks: {}, documents: {}, sampleResponses: [] };

  await recordSampleResponses(manifest);

  await capture(manifest, 'api/config');
  await capture(manifest, 'api/endpoints');
  await capture(manifest, 'api/metrics');
  await capture(manifest, 'api/metrics/components');
  await capture(manifest, 'api/metrics/routes');
  await capture(manifest, 'api/traces');
  await capture(manifest, 'api/traces/live');
  await capture(manifest, 'api/deploys');

  const traces = await getJson('api/traces');
  const summaries = traces?.items ?? [];
  if (summaries.length === 0) {
    throw new Error('The sample app returned no traces. Is it running, with NetApiFlow mounted?');
  }

  for (const summary of summaries) {
    await capture(manifest, `api/traces/${summary.traceId}`);
  }

  for (const recording of manifest.sampleResponses) {
    await capture(manifest, `api/traces/${recording.traceId}`);
  }

  useFirstTraceForTracesWithoutRecording(manifest, summaries[0].traceId);

  for (const summary of distinctRoutes(summaries)) {
    const canonicalKey = `method=${summary.method}&route=${summary.routeTemplate}`;
    const escapedQuery = `method=${encodeURIComponent(summary.method)}&route=${encodeURIComponent(summary.routeTemplate)}`;
    await capture(manifest, `api/architecture?${canonicalKey}`, `api/architecture?${escapedQuery}`);
  }

  await captureOpenApiDocumentTheRunnerReads(manifest);
  await anchorBlueprints(manifest);
  await writeManifest(manifest);

  console.log(`Wrote ${Object.keys(manifest.routes).length} fixtures to ${outputDirectory}`);
}

async function writeManifest(manifest) {
  await writeFile(
    join(outputDirectory, 'manifest.json'),
    `${JSON.stringify(manifest, null, 2)}\n`,
    'utf8',
  );
}

async function anchorBlueprintsOfTheRecordedFixtures() {
  const manifest = JSON.parse(await readFile(join(outputDirectory, 'manifest.json'), 'utf8'));
  for (const key of Object.keys(manifest.routes).filter((name) => name.startsWith('api/blueprint'))) {
    delete manifest.routes[key];
  }

  await anchorBlueprints(manifest);
  await writeManifest(manifest);
  console.log(
    `Anchored ${Object.keys(manifest.routes).filter((name) => name.startsWith('api/blueprint')).length} blueprints, ${manifest.noContent.length} answered with no content.`,
  );
}

async function anchorBlueprints(manifest) {
  const traces = await readRecordedTraces(manifest);
  const noContent = [];

  for (const trace of traces) {
    const key = `api/blueprint?before=${trace.startedAt}&method=${trace.method}&route=${trace.routeTemplate}`;
    const earlier = lastSuccessBefore(traces, trace);
    if (earlier) {
      manifest.routes[key] = earlier.file;
    } else {
      noContent.push(key);
    }
  }

  manifest.noContent = noContent.sort();
}

async function readRecordedTraces(manifest) {
  const traces = [];
  for (const [key, file] of Object.entries(manifest.routes)) {
    if (/^api\/traces\/[0-9a-f]{32}$/.test(key)) {
      const detail = JSON.parse(await readFile(join(outputDirectory, file), 'utf8'));
      traces.push({ ...detail, file });
    }
  }

  return traces;
}

function lastSuccessBefore(traces, trace) {
  return traces
    .filter(
      (candidate) =>
        candidate.status === 'Success' &&
        candidate.method.toUpperCase() === trace.method.toUpperCase() &&
        routeKey(candidate.routeTemplate) === routeKey(trace.routeTemplate) &&
        startedAtMicros(candidate.startedAt) < startedAtMicros(trace.startedAt),
    )
    .sort((left, right) => startedAtMicros(right.startedAt) - startedAtMicros(left.startedAt))
    .at(0);
}

function routeKey(routeTemplate) {
  const withLeadingSlash = routeTemplate.startsWith('/') ? routeTemplate : `/${routeTemplate}`;
  const withoutTrailingSlash =
    withLeadingSlash.length > 1 && withLeadingSlash.endsWith('/')
      ? withLeadingSlash.slice(0, -1)
      : withLeadingSlash;
  return withoutTrailingSlash.replace(/\{\*?([^{}:?]*)[^{}]*\}/g, '{$1}');
}

function startedAtMicros(startedAt) {
  const [seconds, fraction = ''] = startedAt.replace('Z', '').split('.');
  return Date.parse(`${seconds}Z`) * 1000 + Number(fraction.padEnd(6, '0').slice(0, 6));
}

function useFirstTraceForTracesWithoutRecording(manifest, firstTraceId) {
  manifest.fallbacks['api/traces'] = manifest.routes[`api/traces/${firstTraceId}`];
}

function distinctRoutes(summaries) {
  const byMethodAndTemplate = new Map();
  for (const summary of summaries) {
    byMethodAndTemplate.set(`${summary.method} ${summary.routeTemplate}`, summary);
  }

  return byMethodAndTemplate.values();
}

async function warmUp() {
  await rm(outputDirectory, { recursive: true, force: true });
  await mkdir(outputDirectory, { recursive: true });

  await runScenarios();
  await waitForHistoryToReportTraces();
}

async function runScenarios() {
  for (const scenario of SCENARIOS) {
    for (let attempt = 0; attempt < scenario.times; attempt++) {
      const delay = attempt % 2 === 0 ? '250' : '0';
      await fetch(`${options.baseUrl}${scenario.path}`, {
        headers: { [DELAY_HEADER]: delay },
      }).catch(() => undefined);
    }
  }
}

async function waitForHistoryToReportTraces() {
  for (let attempt = 0; attempt < 30; attempt++) {
    await new Promise((resolve) => setTimeout(resolve, 1000));
    const traces = await getJson('api/traces');
    if ((traces?.items?.length ?? 0) > 0) {
      return;
    }
  }

  throw new Error('The sample app recorded no traces within 30s. Is it running, with NetApiFlow mounted?');
}

async function captureOpenApiDocumentTheRunnerReads(manifest) {
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
  const filePath = join(outputDirectory, fileName);
  await writeFile(filePath, await formatJson(filePath, await response.text()), 'utf8');
  manifest.documents[documentPath] = fileName;
}

async function recordSampleResponses(manifest) {
  const openApiDocument = await fetchOpenApiDocument();
  if (!openApiDocument) {
    return;
  }

  for (const [template, operations] of Object.entries(openApiDocument.paths)) {
    if (isDashboardRoute(template)) {
      continue;
    }

    for (const method of Object.keys(operations)) {
      if (method.toUpperCase() !== 'GET') {
        console.warn(`Skipped ${method.toUpperCase()} ${template}: only GET routes are recorded.`);
        continue;
      }

      manifest.sampleResponses.push(await recordSampleResponse(template));
    }
  }
}

async function fetchOpenApiDocument() {
  const config = await getJson('api/config');
  if (!config?.openApiDocumentPath) {
    return undefined;
  }

  const response = await fetch(`${options.baseUrl}${config.openApiDocumentPath}`).catch(() => undefined);
  return response?.ok ? response.json() : undefined;
}

function isDashboardRoute(template) {
  return template === options.dashboardPath || template.startsWith(`${options.dashboardPath}/`);
}

async function recordSampleResponse(template) {
  const exampleRoute = template.replace(/\{[^}]+\}/g, SAMPLE_PARAMETER_VALUE);
  const response = await fetch(`${options.baseUrl}${exampleRoute}`);
  if (!response.ok) {
    throw new Error(`GET ${exampleRoute} answered ${response.status}. The sample needs its seeded order ${SAMPLE_PARAMETER_VALUE}.`);
  }

  const traceId = response.headers.get(TRACE_ID_HEADER);
  if (!traceId) {
    throw new Error(`GET ${exampleRoute} answered without ${TRACE_ID_HEADER}. Is EmitTraceIdHeader off?`);
  }

  const contentType = response.headers.get('content-type') ?? 'text/plain';
  const text = await response.text();
  await waitForPersistedTrace(traceId);

  return {
    method: 'GET',
    template,
    status: response.status,
    contentType,
    body: text.length === 0 ? null : await writeSampleBody(template, contentType, text),
    traceId,
  };
}

async function writeSampleBody(template, contentType, text) {
  const isJson = contentType.includes('json');
  const fileName = `sample_GET_${template.replace(/[^a-zA-Z0-9.-]/g, '_')}.${isJson ? 'json' : 'txt'}`;
  const filePath = join(outputDirectory, fileName);
  await writeFile(filePath, isJson ? await formatJson(filePath, text) : text, 'utf8');
  return fileName;
}

async function formatJson(filePath, text) {
  prettier ??= await import(
    pathToFileURL(join(clientAppDirectory, 'node_modules/prettier/index.mjs')).href
  );
  const projectConfig = (await prettier.resolveConfig(filePath)) ?? {};
  return prettier.format(text, { ...projectConfig, filepath: filePath });
}

async function waitForPersistedTrace(traceId) {
  for (let attempt = 0; attempt < 30; attempt++) {
    if ((await getJson(`api/traces/${traceId}`)) !== undefined) {
      return;
    }

    await new Promise((resolve) => setTimeout(resolve, 1000));
  }

  throw new Error(`Trace ${traceId} was not persisted within 30s.`);
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
  const parsed = { baseUrl: 'http://localhost:5080', dashboardPath: '/flow', anchorBlueprintsOnly: false };
  for (let i = 0; i < argv.length; i += 2) {
    if (argv[i] === '--anchor-blueprints-only') {
      parsed.anchorBlueprintsOnly = true;
      i -= 1;
    } else if (argv[i] === '--base-url') {
      parsed.baseUrl = argv[i + 1].replace(/\/$/, '');
    } else if (argv[i] === '--dashboard') {
      parsed.dashboardPath = `/${argv[i + 1].replace(/^\/|\/$/g, '')}`;
    }
  }

  return parsed;
}
