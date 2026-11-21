import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { clientFor, delay, freePort, startApplication, untilReady } from './app-harness.mjs';
import { findBrowser, openBrowser } from './browser-session.mjs';
import { describeEvent } from './verify-built-demo.mjs';

export const CHECKS = { oldTrace: 'live-panel:old-trace', running: 'live-panel:running', ended: 'live-panel:ended' };
export const CHECK_IDS = Object.values(CHECKS);

const ROUTE_PREFIX = '/live-probe/orders';
const WAITING_WORDS = ['Waiting', 'Aguardando'];
const NOT_EXECUTED_WORDS = ['Not executed', 'Não executado'];
const NODES = "[...document.querySelectorAll('.flow-graph__node')].map((node) => node.textContent.replace(/\\s+/g, ' ').trim())";
const POLL_MS = 100;
const SETTLE_MS = 250;
const TIMEOUT_MS = 15_000;
const VIEWPORT = { width: 1280, height: 800 };

export function stateOfNode(text) {
  if (WAITING_WORDS.some((word) => text.includes(word))) {
    return 'waiting';
  }

  return NOT_EXECUTED_WORDS.some((word) => text.includes(word)) ? 'not-executed' : 'other';
}

export function statesOf(nodeTexts) {
  return nodeTexts.map(stateOfNode);
}

async function untilTrue(description, check) {
  const deadline = Date.now() + TIMEOUT_MS;
  while (Date.now() < deadline) {
    const found = await check();
    if (found) {
      return found;
    }

    await delay(POLL_MS);
  }

  throw new Error(`${description} (waited ${TIMEOUT_MS} ms)`);
}

async function listItems(client, path) {
  const body = JSON.parse((await client.get(path)).text);
  return Array.isArray(body) ? body : body.items;
}

async function untilTheHeldRequestIsRunning(client) {
  return untilTrue('the held request never showed in the live list with its route', async () => {
    const items = await listItems(client, '/flow/api/traces/live');
    return items.find((item) => item.routeTemplate.startsWith(ROUTE_PREFIX))?.traceId;
  });
}

async function untilBothExecutionsAreStored(client) {
  return untilTrue(`the two executions of ${ROUTE_PREFIX} were not stored`, async () => {
    const items = (await listItems(client, '/flow/api/traces?pageSize=50')).filter((item) => item.routeTemplate.startsWith(ROUTE_PREFIX));
    return items.length >= 2 ? items : undefined;
  });
}

function watchPage(browser) {
  const record = { problems: [], blueprintRequests: new Map() };
  browser.onEvent((message) => {
    const problem = describeEvent(message);
    if (problem !== undefined) {
      record.problems.push(problem);
    }

    const { method, params } = message;
    if (method === 'Network.requestWillBeSent' && params.request.url.includes('/blueprint')) {
      record.blueprintRequests.set(params.requestId, false);
    } else if (method === 'Network.loadingFinished' && record.blueprintRequests.has(params.requestId)) {
      record.blueprintRequests.set(params.requestId, true);
    }
  });

  return {
    problems: () => record.problems,
    blueprintAnswered: () => [...record.blueprintRequests.values()].some(Boolean),
    reset: () => {
      record.problems.length = 0;
      record.blueprintRequests.clear();
    },
  };
}

async function nodeTexts(browser) {
  return browser.evaluate(NODES);
}

async function settledNodeTexts(browser) {
  let previous = JSON.stringify(await nodeTexts(browser));
  for (;;) {
    await delay(SETTLE_MS);
    const current = JSON.stringify(await nodeTexts(browser));
    if (current === previous) {
      return JSON.parse(current);
    }

    previous = current;
  }
}

async function openTrace(browser, page, baseUrl, traceId) {
  page.reset();
  await browser.send('Page.navigate', { url: `${baseUrl}/flow/?trace=${traceId}` });
  await untilTrue('the graph drew no node', async () => (await nodeTexts(browser)).length > 0);
}

async function check(id, title, body, outcomes) {
  try {
    await body();
    console.log(`PASS ${id} ${title}`);
    outcomes.push(true);
  } catch (error) {
    console.error(`FAIL ${id} ${title}\n     ${error.message}`);
    outcomes.push(false);
  }
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const port = await freePort();
  const application = startApplication(options.dll, { environment: 'Development', variables: {} }, port);
  const baseUrl = `http://127.0.0.1:${port}`;
  const client = clientFor(baseUrl);
  const outcomes = [];
  let browser;

  try {
    await untilReady(client, application, '/hello');
    await client.send({ method: 'DELETE', path: '/flow/api/traces' });

    await client.get(`${ROUTE_PREFIX}/1?cached=true&hold=false`);
    await client.get(`${ROUTE_PREFIX}/2?cached=false&hold=false`);
    const stored = await untilBothExecutionsAreStored(client);
    const older = stored.reduce((first, item) => (Date.parse(item.startedAt) < Date.parse(first.startedAt) ? item : first));

    browser = await openBrowser({
      executablePath: options.browser ?? (await findBrowser()),
      port: await freePort(),
      profileDirectory: options.profileDirectory,
      sandbox: options.sandbox,
    });
    for (const domain of ['Runtime', 'Log', 'Page', 'Network']) {
      await browser.send(`${domain}.enable`);
    }

    await browser.send('Network.setCacheDisabled', { cacheDisabled: true });
    await browser.send('Emulation.setDeviceMetricsOverride', { ...VIEWPORT, deviceScaleFactor: 1, mobile: false });
    const page = watchPage(browser);

    await check(
      CHECKS.oldTrace,
      'an older trace is drawn as it ran: a later execution that ran more steps does not turn them into not executed',
      async () => {
        await openTrace(browser, page, baseUrl, older.traceId);
        await untilTrue('the panel never asked for the blueprint, so this check proved nothing', async () => page.blueprintAnswered());
        const states = statesOf(await settledNodeTexts(browser));
        if (states.includes('not-executed')) {
          throw new Error(`the older trace shows steps of a later execution as not executed: ${(await nodeTexts(browser)).join(' | ')}`);
        }

        if (page.problems().length > 0) {
          throw new Error(`console or network problems: ${page.problems().join(' | ')}`);
        }
      },
      outcomes,
    );

    await client.get('/live-probe/reset');
    const held = fetch(`${baseUrl}${ROUTE_PREFIX}/3?cached=true&hold=true`).then((response) => response.text());
    try {
      const runningTraceId = await untilTheHeldRequestIsRunning(client);

      await check(
        CHECKS.running,
        'a trace that is still running draws the steps it is expected to run as waiting, with its route known',
        async () => {
          await openTrace(browser, page, baseUrl, runningTraceId);
          await untilTrue('no step is drawn as waiting while the request runs', async () => statesOf(await nodeTexts(browser)).includes('waiting'));
        },
        outcomes,
      );
    } finally {
      await client.get('/live-probe/release');
      await held.catch(() => undefined);
    }

    await check(
      CHECKS.ended,
      'the same steps are drawn as not executed once the request ended without running them',
      async () => {
        await untilTrue('no step is drawn as not executed after the request ended', async () => statesOf(await nodeTexts(browser)).includes('not-executed'));
        const states = statesOf(await settledNodeTexts(browser));
        if (states.includes('waiting')) {
          throw new Error(`steps are still drawn as waiting after the request ended: ${(await nodeTexts(browser)).join(' | ')}`);
        }
      },
      outcomes,
    );
  } catch (error) {
    console.error(`${error.message}\n${application.logTail()}`);
    outcomes.push(false);
  } finally {
    await browser?.close();
    await application.stop();
  }

  console.log(`\nSUMMARY live-panel passed=${outcomes.filter(Boolean).length} of ${outcomes.length}`);
  process.exit(outcomes.length > 0 && outcomes.every(Boolean) ? 0 : 1);
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
    if (name === '--dll') {
      options.dll = resolve(value);
    } else if (name === '--browser') {
      options.browser = value;
    } else if (name === '--profile-directory') {
      options.profileDirectory = resolve(value);
    } else {
      throw new Error(`Unknown argument "${name}". Use --dll <UserApp.dll> [--browser <path>] [--profile-directory <folder>] [--sandbox].`);
    }
  }

  if (!options.dll) {
    throw new Error('Pass --dll, the built app that has the live-probe endpoints.');
  }

  return options;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  await main();
}
