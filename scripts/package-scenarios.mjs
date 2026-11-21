import { pathToFileURL } from 'node:url';
import { clientFor as baseClientFor, delay, freePort, startApplication, untilReady } from './app-harness.mjs';

const TRACE_ID_HEADER = 'x-netapiflow-traceid';
const TRACE_ID_PATTERN = /^[0-9a-f]{32}$/;
const HOSTILE_NAME = 'evil.example';
const HOSTILE_ORIGIN = `http://${HOSTILE_NAME}`;
const STORED_TRACE_TIMEOUT_MS = 15_000;

const INITIALIZE_BODY = JSON.stringify({
  jsonrpc: '2.0',
  id: 1,
  method: 'initialize',
  params: { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'package-scenarios', version: '1.0.0' } },
});
const MCP_HEADERS = { 'content-type': 'application/json', accept: 'application/json, text/event-stream' };

export const VARIANTS = {
  development: { environment: 'Development', variables: {} },
  production: { environment: 'Production', variables: {} },
  'production-header-with-handler': {
    environment: 'Production',
    variables: { NetApiFlow__EmitTraceIdHeader: 'true', USER_EXCEPTION_HANDLER: 'true' },
  },
  'production-header-without-handler': {
    environment: 'Production',
    variables: { NetApiFlow__EmitTraceIdHeader: 'true' },
  },
};

export const SCENARIOS = [
  {
    id: 'b',
    variant: 'development',
    title: 'a request to the app records a trace with the Endpoint step and the Service and Repository steps found by convention',
    run: async (app) => {
      const response = await app.get('/orders/1');
      expectStatus(response, 200, 'GET /orders/1');
      const traceId = response.headers[TRACE_ID_HEADER];
      expect(TRACE_ID_PATTERN.test(traceId ?? ''), `GET /orders/1 answered without a valid ${TRACE_ID_HEADER} header (got "${traceId}")`);

      const detail = await untilStored(app, `/flow/api/traces/${traceId}`);
      const types = detail.spans.map((span) => String(span.type));
      for (const wanted of ['Endpoint', 'Service', 'Repository']) {
        expect(types.includes(wanted), `the trace has no ${wanted} step, its steps are: ${types.join(', ')}`);
      }

      const listing = await app.get('/flow/api/traces?pageSize=100');
      expectStatus(listing, 200, 'GET /flow/api/traces');
      expect(
        JSON.parse(listing.text).items.some((item) => item.traceId === traceId),
        'the list of traces does not contain the trace that was just recorded',
      );
    },
  },
  {
    id: 'c',
    variant: 'development',
    title: 'the OpenAPI document of the app has no path that starts with /flow',
    run: async (app) => {
      const response = await app.get('/openapi/v1.json');
      expectStatus(response, 200, 'GET /openapi/v1.json');
      const paths = Object.keys(JSON.parse(response.text).paths ?? {});
      expect(paths.includes('/orders/{id}'), `the document does not list the app's own route, its paths are: ${paths.join(', ')}`);
      const ours = paths.filter((path) => path.startsWith('/flow'));
      expect(ours.length === 0, `the document lists routes of NetApiFlow: ${ours.join(', ')}`);
    },
  },
  {
    id: 'd',
    variant: 'development',
    title: 'the dashboard refuses a foreign Host and a foreign Origin with 403 and answers its own host with 200',
    run: async (app) => {
      expectStatus(await app.get('/flow/api/config', { host: HOSTILE_NAME }), 403, `GET /flow/api/config with Host: ${HOSTILE_NAME}`);
      expectStatus(await app.get('/flow/api/config', { origin: HOSTILE_ORIGIN }), 403, `GET /flow/api/config with Origin: ${HOSTILE_ORIGIN}`);
      expectStatus(await app.get('/flow/api/config'), 200, 'GET /flow/api/config with the host of the app');
    },
  },
  {
    id: 'e',
    variant: 'development',
    title: 'the page, the API and the refusal of the dashboard carry the security headers, and the page policy has no unsafe-inline script',
    run: async (app) => {
      const page = await app.get('/flow/');
      expectStatus(page, 200, 'GET /flow/');
      expectSecurityHeaders(page, 'the page /flow/');
      const scriptPolicy = directive(page.headers['content-security-policy'], 'script-src');
      expect(scriptPolicy !== undefined, "the page's Content-Security-Policy has no script-src directive");
      expect(scriptPolicy.includes("'self'"), `script-src of the page does not allow 'self': ${scriptPolicy.join(' ')}`);
      expect(!scriptPolicy.includes("'unsafe-inline'"), `script-src of the page allows 'unsafe-inline': ${scriptPolicy.join(' ')}`);

      const api = await app.get('/flow/api/config');
      expectStatus(api, 200, 'GET /flow/api/config');
      expectSecurityHeaders(api, 'the response of /flow/api/config');

      const refusal = await app.get('/flow/api/config', { host: HOSTILE_NAME });
      expectStatus(refusal, 403, `GET /flow/api/config with Host: ${HOSTILE_NAME}`);
      expectSecurityHeaders(refusal, 'the 403 of /flow/api/config');
    },
  },
  {
    id: 'f',
    variant: 'development',
    title: 'the trace id header is present in Development, also on a 500 answered by the developer exception page',
    run: async (app) => {
      expectTraceIdHeader(await app.get('/orders/1'), true, 'GET /orders/1');
      const failure = await app.get('/boom');
      expectStatus(failure, 500, 'GET /boom');
      expectTraceIdHeader(failure, true, 'the 500 of GET /boom');
    },
  },
  {
    id: 'f',
    variant: 'production',
    title: 'the trace id header is absent in Production by default',
    run: async (app) => {
      expectTraceIdHeader(await app.get('/orders/1'), false, 'GET /orders/1');
    },
  },
  {
    id: 'f',
    variant: 'production-header-with-handler',
    title: 'with EmitTraceIdHeader and UseExceptionHandler the header is present in Production, also on the 500',
    run: async (app) => {
      expectTraceIdHeader(await app.get('/orders/1'), true, 'GET /orders/1');
      const failure = await app.get('/boom');
      expectStatus(failure, 500, 'GET /boom');
      expectTraceIdHeader(failure, true, 'the 500 of GET /boom');
    },
  },
  {
    id: 'f',
    variant: 'production-header-without-handler',
    title: 'with EmitTraceIdHeader and no exception handler the header is present, except on the 500 the server builds itself',
    run: async (app) => {
      expectTraceIdHeader(await app.get('/orders/1'), true, 'GET /orders/1');
      const failure = await app.get('/boom');
      expectStatus(failure, 500, 'GET /boom');
      expectTraceIdHeader(failure, false, 'the 500 of GET /boom');
    },
  },
  {
    id: 'g',
    variant: 'production',
    title: 'in Production, with no authorization configured, the dashboard and the MCP answer 404, and a foreign Origin gets 403, not 404',
    run: async (app) => {
      expectStatus(await app.get('/flow/'), 404, 'GET /flow/');
      expectStatus(await app.get('/flow/api/traces'), 404, 'GET /flow/api/traces');
      expectStatus(await app.postMcp({}), 404, 'POST /flow/mcp initialize');
      expectStatus(await app.get('/flow/api/traces', { origin: HOSTILE_ORIGIN }), 403, `GET /flow/api/traces with Origin: ${HOSTILE_ORIGIN}`);
      expectStatus(await app.postMcp({ origin: HOSTILE_ORIGIN }), 403, `POST /flow/mcp initialize with Origin: ${HOSTILE_ORIGIN}`);
    },
  },
  {
    id: 'h',
    variant: 'development',
    title: 'the MCP endpoint refuses a foreign Origin and a foreign Host with 403, answers its own host, and carries the security headers',
    run: async (app) => {
      const accepted = await app.postMcp({});
      expectStatus(accepted, 200, 'POST /flow/mcp initialize with the host of the app');
      expectSecurityHeaders(accepted, 'the answer of /flow/mcp');

      const foreignOrigin = await app.postMcp({ origin: HOSTILE_ORIGIN });
      expectStatus(foreignOrigin, 403, `POST /flow/mcp initialize with Origin: ${HOSTILE_ORIGIN}`);
      expectSecurityHeaders(foreignOrigin, `the 403 of /flow/mcp for Origin: ${HOSTILE_ORIGIN}`);

      const foreignHost = await app.postMcp({ host: HOSTILE_NAME });
      expectStatus(foreignHost, 403, `POST /flow/mcp initialize with Host: ${HOSTILE_NAME}`);
      expectSecurityHeaders(foreignHost, `the 403 of /flow/mcp for Host: ${HOSTILE_NAME}`);
    },
  },
];

class ScenarioFailure extends Error {}

function expect(condition, message) {
  if (!condition) {
    throw new ScenarioFailure(message);
  }
}

function expectStatus(response, wanted, label) {
  expect(response.status === wanted, `${label} answered ${response.status}, expected ${wanted}`);
}

function expectTraceIdHeader(response, present, label) {
  const value = response.headers[TRACE_ID_HEADER];
  if (present) {
    expect(TRACE_ID_PATTERN.test(value ?? ''), `${label} has no valid ${TRACE_ID_HEADER} header (got "${value}")`);
  } else {
    expect(value === undefined, `${label} carries ${TRACE_ID_HEADER}: ${value}, expected none`);
  }
}

function expectSecurityHeaders(response, label) {
  expect(response.headers['x-content-type-options'] === 'nosniff', `${label} has X-Content-Type-Options "${response.headers['x-content-type-options']}", expected nosniff`);
  expect(response.headers['x-frame-options'] === 'DENY', `${label} has X-Frame-Options "${response.headers['x-frame-options']}", expected DENY`);
  const frameAncestors = directive(response.headers['content-security-policy'], 'frame-ancestors');
  expect(
    frameAncestors !== undefined && frameAncestors.length === 1 && frameAncestors[0] === "'none'",
    `${label} has Content-Security-Policy "${response.headers['content-security-policy']}", expected frame-ancestors 'none'`,
  );
}

function directive(policy, name) {
  const found = (policy ?? '')
    .split(';')
    .map((part) => part.trim().split(/\s+/))
    .find(([directiveName]) => directiveName === name);
  return found?.slice(1);
}

async function untilStored(app, path) {
  const deadline = Date.now() + STORED_TRACE_TIMEOUT_MS;
  let last;
  while (Date.now() < deadline) {
    last = await app.get(path);
    if (last.status === 200) {
      return JSON.parse(last.text);
    }

    await delay(100);
  }

  throw new ScenarioFailure(`${path} was still answering ${last?.status} after ${STORED_TRACE_TIMEOUT_MS} ms, the trace was never stored`);
}

function clientFor(baseUrl) {
  const client = baseClientFor(baseUrl);
  return {
    ...client,
    postMcp: (headers) => client.send({ method: 'POST', path: '/flow/mcp', headers: { ...MCP_HEADERS, ...headers }, body: INITIALIZE_BODY }),
  };
}

async function runScenarios(app, scenarios) {
  const failures = [];
  for (const scenario of scenarios) {
    try {
      await scenario.run(app);
      console.log(`PASS ${scenario.id} [${scenario.variant}] ${scenario.title}`);
    } catch (error) {
      failures.push(scenario);
      console.error(`FAIL ${scenario.id} [${scenario.variant}] ${scenario.title}\n     ${error.message}`);
    }
  }

  return failures;
}

function scenariosOf(variantName) {
  return SCENARIOS.filter((scenario) => scenario.variant === variantName);
}

async function runAgainstNewApplications(dll, variantNames) {
  let failed = 0;
  let total = 0;
  for (const variantName of variantNames) {
    const scenarios = scenariosOf(variantName);
    const port = await freePort();
    const application = startApplication(dll, VARIANTS[variantName], port);
    try {
      const app = clientFor(`http://127.0.0.1:${port}`);
      await untilReady(app, application);
      console.log(`\n== ${variantName}: ${VARIANTS[variantName].environment}, ${scenarios.length} scenarios`);
      const failures = await runScenarios(app, scenarios);
      total += scenarios.length;
      failed += failures.length;
      if (failures.length > 0) {
        console.error(`\nLast lines of the app log (${variantName}):\n${application.logTail()}`);
      }
    } finally {
      await application.stop();
    }
  }

  console.log(`\nSUMMARY scenarios=${total} failed=${failed}`);
  return failed;
}

async function runRepeatedlyAgainstStandingApplication(baseUrl, variantName, repeat) {
  const app = clientFor(baseUrl);
  await untilReady(app);
  const scenarios = scenariosOf(variantName);
  for (let round = 1; round <= repeat; round++) {
    const failures = await runScenarios(app, scenarios);
    if (failures.length > 0) {
      console.error(`\nSUMMARY round=${round} of ${repeat} failed=${failures.length}`);
      return failures.length;
    }
  }

  console.log(`\nSUMMARY ${repeat} rounds of ${scenarios.length} scenarios, no failure`);
  return 0;
}

function parseArguments(argumentList) {
  const options = { variants: Object.keys(VARIANTS), repeat: 1 };
  for (let index = 0; index < argumentList.length; index++) {
    const name = argumentList[index];
    const value = argumentList[index + 1];
    if (name === '--list') {
      options.list = true;
    } else if (name === '--dll') {
      options.dll = value;
      index++;
    } else if (name === '--base-url') {
      options.baseUrl = value;
      index++;
    } else if (name === '--variant') {
      options.variants = [value];
      index++;
    } else if (name === '--variants') {
      options.variants = value.split(',');
      index++;
    } else if (name === '--repeat') {
      options.repeat = Number(value);
      index++;
    } else {
      throw new Error(`Unknown argument "${name}". Use --list, or --dll <UserApp.dll> [--variants a,b], or --base-url <url> --variant <name> [--repeat <n>].`);
    }
  }

  const unknown = options.variants.filter((variantName) => !(variantName in VARIANTS));
  if (unknown.length > 0) {
    throw new Error(`Unknown variant ${unknown.join(', ')}. The variants are: ${Object.keys(VARIANTS).join(', ')}.`);
  }

  return options;
}

async function main() {
  const options = parseArguments(process.argv.slice(2));

  if (options.list) {
    console.log(JSON.stringify(SCENARIOS.map(({ id, variant, title }) => ({ id, variant, title })), null, 2));
    return;
  }

  let failed;
  if (options.baseUrl) {
    failed = await runRepeatedlyAgainstStandingApplication(options.baseUrl, options.variants[0], options.repeat);
  } else if (options.dll) {
    failed = await runAgainstNewApplications(options.dll, options.variants);
  } else {
    throw new Error('Pass --dll <UserApp.dll> to start the app itself, or --base-url <url> to use one already standing.');
  }

  process.exit(failed > 0 ? 1 : 0);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  await main();
}
