import { createServer } from 'node:http';
import { createServer as createSocketServer } from 'node:net';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { findBrowser, openBrowser } from './browser-session.mjs';

const MANIFEST_PATH = 'assets/demo-fixtures/manifest.json';
const TRACE_ID_ROUTE = /^api\/traces\/([0-9a-f]{32})$/;
const READY_TIMEOUT_MS = 20_000;
const QUIET_MS = 600;
const QUIET_TIMEOUT_MS = 10_000;
const POLL_MS = 100;
const VIEWPORT = { width: 1280, height: 800 };

const TRACE_READY = "document.querySelectorAll('.flow-graph__node').length > 0";
const SCREENS = [
  { path: '/history', ready: "document.querySelectorAll('app-history tr').length > 1" },
  { path: '/components', ready: "document.querySelectorAll('app-components mat-card').length > 0" },
  { path: '/runner', ready: "document.querySelector('app-runner app-endpoint-form') !== null" },
];

const MIME_TYPES = {
  '.html': 'text/html',
  '.js': 'text/javascript',
  '.css': 'text/css',
  '.json': 'application/json',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
  '.woff2': 'font/woff2',
  '.txt': 'text/plain',
};

function delay(milliseconds) {
  return new Promise((resolveDelay) => setTimeout(resolveDelay, milliseconds));
}

export function tracesOfManifest(manifest) {
  return Object.keys(manifest.routes)
    .map((key) => TRACE_ID_ROUTE.exec(key)?.[1])
    .filter((traceId) => traceId !== undefined);
}

export function describeEvent(message) {
  const { method, params } = message;
  if (method === 'Runtime.consoleAPICalled' && params.type === 'error') {
    const argument = params.args[0];
    return `console.error: ${String(argument?.description ?? argument?.value ?? '').slice(0, 200)}`;
  }

  if (method === 'Runtime.exceptionThrown') {
    const details = params.exceptionDetails;
    return `pageerror: ${String(details.exception?.description ?? details.text).slice(0, 200)}`;
  }

  if (method === 'Log.entryAdded' && params.entry.level === 'error') {
    return `log.error: ${params.entry.text.slice(0, 160)} ${params.entry.url ?? ''}`.trim();
  }

  if (method === 'Network.responseReceived' && params.response.status >= 400) {
    return `HTTP ${params.response.status}: ${params.response.url}`;
  }

  if (method === 'Network.loadingFailed' && !params.canceled) {
    return `request failed (${params.errorText}): ${params.requestId}`;
  }

  return undefined;
}

function isTrackedUrl(url) {
  return !url.startsWith('data:') && !url.startsWith('blob:');
}

export function createPageWatcher() {
  const problems = [];
  const pending = new Map();
  let lastActivityAt = Date.now();

  return {
    problems,
    pending,
    onEvent(message) {
      lastActivityAt = Date.now();
      const { method, params } = message;
      if (method === 'Network.requestWillBeSent' && isTrackedUrl(params.request.url)) {
        pending.set(params.requestId, params.request.url);
      } else if (method === 'Network.loadingFinished' || method === 'Network.loadingFailed') {
        pending.delete(params.requestId);
      }

      const problem = describeEvent(message);
      if (problem !== undefined) {
        problems.push(problem);
      }
    },
    quietFor: () => (pending.size === 0 ? Date.now() - lastActivityAt : 0),
    unanswered: () => [...pending.values()].map((url) => `no answer: ${url}`),
  };
}

async function untilTrue(browser, expression, description) {
  const deadline = Date.now() + READY_TIMEOUT_MS;
  while (Date.now() < deadline) {
    if (await browser.evaluate(expression)) {
      return true;
    }

    await delay(POLL_MS);
  }

  return description;
}

async function untilQuiet(watcher) {
  const deadline = Date.now() + QUIET_TIMEOUT_MS;
  while (Date.now() < deadline && watcher.quietFor() < QUIET_MS) {
    await delay(POLL_MS);
  }
}

async function visit(browser, url, ready) {
  const watcher = createPageWatcher();
  browser.onEvent((message) => watcher.onEvent(message));
  await browser.send('Page.navigate', { url });

  const outcome = await untilTrue(browser, ready, `the page did not draw what marks it as ready (${ready}) in ${READY_TIMEOUT_MS} ms`);
  await untilQuiet(watcher);

  const problems = [...watcher.problems, ...watcher.unanswered()];
  if (outcome !== true) {
    problems.push(outcome);
  }

  return problems;
}

async function startStaticServer(directory) {
  const server = createServer(async (request, response) => {
    const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
    const requested = join(directory, pathname);
    const isFile = await stat(requested).then((entry) => entry.isFile(), () => false);
    const isRoute = extname(pathname) === '';
    if (!isFile && !isRoute) {
      response.writeHead(404, { 'Cache-Control': 'no-store' });
      response.end();
      return;
    }

    const file = isFile ? requested : join(directory, 'index.html');
    response.writeHead(200, { 'Content-Type': MIME_TYPES[extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
    response.end(await readFile(file));
  });
  await new Promise((resolveListening) => server.listen(0, '127.0.0.1', resolveListening));
  return { url: `http://127.0.0.1:${server.address().port}`, close: () => new Promise((resolveClosed) => server.close(resolveClosed)) };
}

function freePort() {
  return new Promise((resolvePort, reject) => {
    const probe = createSocketServer();
    probe.on('error', reject);
    probe.listen(0, '127.0.0.1', () => {
      const { port } = probe.address();
      probe.close(() => resolvePort(port));
    });
  });
}

export function summarize(results, expected) {
  const clean = results.filter((result) => result.problems.length === 0).length;
  return clean === expected ? `0 errors in ${clean} of ${expected}` : `errors found: only ${clean} of ${expected} are clean`;
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const manifest = JSON.parse(await readFile(join(options.directory, MANIFEST_PATH), 'utf8'));
  const traceIds = tracesOfManifest(manifest);
  if (traceIds.length === 0) {
    throw new Error(`The manifest ${MANIFEST_PATH} lists no trace, so there is nothing to open.`);
  }

  const server = await startStaticServer(options.directory);
  const browser = await openBrowser({
    executablePath: options.browser ?? (await findBrowser()),
    port: await freePort(),
    profileDirectory: options.profileDirectory,
    sandbox: options.sandbox,
  });

  const traces = [];
  const screens = [];
  try {
    await browser.send('Runtime.enable');
    await browser.send('Log.enable');
    await browser.send('Page.enable');
    await browser.send('Network.enable');
    await browser.send('Network.setCacheDisabled', { cacheDisabled: true });
    await browser.send('Emulation.setDeviceMetricsOverride', { ...VIEWPORT, deviceScaleFactor: 1, mobile: false });
    await browser.send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'light' }] });

    for (const traceId of traceIds) {
      const problems = await visit(browser, `${server.url}/?trace=${traceId}`, TRACE_READY);
      traces.push({ name: `trace ${traceId}`, problems });
      console.log(`${problems.length === 0 ? 'OK  ' : 'FAIL'} trace ${traceId}${problems.length > 0 ? `\n       ${problems.join('\n       ')}` : ''}`);
    }

    for (const screen of SCREENS) {
      const problems = await visit(browser, `${server.url}${screen.path}`, screen.ready);
      screens.push({ name: screen.path, problems });
      console.log(`${problems.length === 0 ? 'OK  ' : 'FAIL'} screen ${screen.path}${problems.length > 0 ? `\n       ${problems.join('\n       ')}` : ''}`);
    }
  } finally {
    await browser.close();
    await server.close();
  }

  console.log(`\nTRACES ${summarize(traces, traceIds.length)} traces of the manifest`);
  console.log(`SCREENS ${summarize(screens, SCREENS.length)} screens`);

  const visitedEveryTrace = traces.length === traceIds.length;
  const failed = [...traces, ...screens].some((result) => result.problems.length > 0);
  if (!visitedEveryTrace || failed) {
    process.exit(1);
  }
}

function parseArguments(argumentList) {
  const options = { sandbox: false };
  for (let index = 0; index < argumentList.length; index++) {
    const name = argumentList[index];
    if (name === '--sandbox') {
      options.sandbox = true;
      continue;
    }

    const value = argumentList[++index];
    if (name === '--directory') {
      options.directory = resolve(value);
    } else if (name === '--browser') {
      options.browser = value;
    } else if (name === '--profile-directory') {
      options.profileDirectory = resolve(value);
    } else {
      throw new Error(`Unknown argument "${name}". Use --directory <dist/demo/browser> [--browser <path>] [--profile-directory <folder>] [--sandbox].`);
    }
  }

  if (!options.directory) {
    throw new Error('Pass --directory, the folder of the built demo (dist/demo/browser).');
  }

  return options;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  await main();
}
