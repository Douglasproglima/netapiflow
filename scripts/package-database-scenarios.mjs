import { randomBytes } from 'node:crypto';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { fileURLToPath } from 'node:url';
import { clientFor, delay, freePort, startApplication, untilReady } from './app-harness.mjs';

const TRACE_ID_HEADER = 'x-netapiflow-traceid';
const READY_PATH = '/ready';
const STORED_TIMEOUT_MS = 20_000;
const SEVERAL_COMMANDS_SPAN_COUNT = 5;
const EXPECTED_COMMANDS = ['SELECT users', 'SELECT orders', 'SELECT users'];
const TABLES_IN_ORDER = ['users', 'orders', 'users'];

const repoRoot = join(fileURLToPath(new URL('.', import.meta.url)), '..');

export const KINDS = {
  postgres: {
    component: 'Npgsql',
    tableQualifier: '',
    controller: 'PostgresController',
    schemaSql: join(repoRoot, 'src/NetApiFlow.Storage.PostgreSql/schema.sql'),
  },
  sqlserver: {
    component: 'SqlClient',
    tableQualifier: 'dbo.',
    controller: 'SqlServerController',
    schemaSql: join(repoRoot, 'src/NetApiFlow.Storage.SqlServer/schema.sql'),
  },
};

export const SCENARIOS = [
  { id: 'several-commands', title: 'several commands in one method record one step each, under the controller, with the SQL and without moving the current activity' },
  { id: 'filters', title: 'the list of traces filters by route and by status' },
  { id: 'restart', title: 'the traces recorded before a restart of the host are listed again, with their steps' },
  { id: 'schema-script', title: 'the schema script of the repository is enough for AutoCreateSchema = false', onlyFor: 'new' },
];

export const CHECK_IDS = Object.keys(KINDS).flatMap((kind) => SCENARIOS.map((scenario) => `database:${kind}:${scenario.id}`));

class ScenarioFailure extends Error {}

function expect(condition, message) {
  if (!condition) {
    throw new ScenarioFailure(message);
  }
}

function variablesFor(kind, connection, prefix, extra = {}) {
  return {
    DATABASE_KIND: kind,
    DATABASE_CONNECTION: connection,
    DATABASE_TABLE_PREFIX: prefix,
    ...extra,
  };
}

async function startHost(dll, variables) {
  const port = await freePort();
  const application = startApplication(dll, { environment: 'Development', variables }, port);
  const app = clientFor(`http://127.0.0.1:${port}`);
  await untilReady(app, application, READY_PATH);
  return { app, application };
}

async function untilTraceStored(app, traceId, minimumSpans) {
  const deadline = Date.now() + STORED_TIMEOUT_MS;
  let lastSeen = 'no answer';
  while (Date.now() < deadline) {
    const answer = await app.get(`/flow/api/traces/${traceId}`);
    if (answer.status === 200) {
      const detail = JSON.parse(answer.text);
      if (detail.spans.length >= minimumSpans) {
        return detail;
      }

      lastSeen = `${detail.spans.length} steps`;
    } else {
      lastSeen = `HTTP ${answer.status}`;
    }

    await delay(250);
  }

  throw new ScenarioFailure(`the trace ${traceId} was not stored with ${minimumSpans} steps within ${STORED_TIMEOUT_MS} ms (last seen: ${lastSeen})`);
}

async function untilListed(app, traceIds) {
  const deadline = Date.now() + STORED_TIMEOUT_MS;
  while (Date.now() < deadline) {
    const listed = (await listTraces(app)).map((item) => item.traceId);
    if (traceIds.every((traceId) => listed.includes(traceId))) {
      return;
    }

    await delay(250);
  }

  throw new ScenarioFailure(`the traces ${traceIds.join(', ')} were not listed from the database within ${STORED_TIMEOUT_MS} ms`);
}

async function listTraces(app, query = '') {
  const answer = await app.get(`/flow/api/traces?pageSize=100${query}`);
  expect(answer.status === 200, `GET /flow/api/traces${query} answered ${answer.status}`);
  return JSON.parse(answer.text).items;
}

async function callAndGetTraceId(app, path, expectedStatus) {
  const answer = await app.get(path);
  expect(answer.status === expectedStatus, `GET ${path} answered ${answer.status}, expected ${expectedStatus}`);
  const traceId = answer.headers[TRACE_ID_HEADER];
  expect(Boolean(traceId), `GET ${path} answered without the ${TRACE_ID_HEADER} header`);
  return { traceId, body: answer.text };
}

function commandsOf(detail, kind) {
  return detail.spans.filter((span) => span.component === KINDS[kind].component).sort((first, second) => first.sequence - second.sequence);
}

export function checkSeveralCommands(detail, kind, body) {
  const controller = detail.spans.find((span) => span.type === 'Controller');
  expect(controller !== undefined, `the trace has no Controller step, its steps are: ${detail.spans.map((span) => `${span.component}/${span.type}`).join(', ')}`);

  const commands = commandsOf(detail, kind);
  expect(
    JSON.stringify(commands.map((command) => command.operation)) === JSON.stringify(EXPECTED_COMMANDS),
    `the ${KINDS[kind].component} steps are [${commands.map((command) => command.operation).join(', ')}], expected one step for each of the three commands: [${EXPECTED_COMMANDS.join(', ')}]`,
  );
  expect(
    commands.every((command) => command.parentSpanId === controller.spanId && command.status === 'Success'),
    'a command step is not a successful child of the controller step',
  );
  const expectedSql = TABLES_IN_ORDER.map((table) => `SELECT count(*) FROM ${KINDS[kind].tableQualifier}${table}`);
  expect(
    JSON.stringify(commands.map((command) => command.tags?.sql)) === JSON.stringify(expectedSql),
    `the SQL of the steps is [${commands.map((command) => command.tags?.sql).join(' | ')}], expected [${expectedSql.join(' | ')}]`,
  );

  const service = detail.spans.find((span) => span.component === 'Service');
  expect(service?.parentSpanId === controller.spanId, 'the step opened after the commands is not a child of the controller step');

  const seen = JSON.parse(body);
  const before = seen.find((entry) => entry.label === 'before');
  const moved = seen.filter((entry) => entry.label !== 'before' && (entry.id !== before.id || entry.isStopped !== false));
  expect(moved.length === 0, `the current activity of the caller changed after ${moved.map((entry) => entry.label).join(', ')}`);
}

async function runSession(context) {
  const { kind, dll, connection } = context;
  const prefix = `p${randomBytes(4).toString('hex')}_`;
  const variables = variablesFor(kind, connection, prefix);
  const outcomes = new Map();
  const attempt = async (id, work) => {
    try {
      await work();
      outcomes.set(id, undefined);
    } catch (error) {
      if (!(error instanceof ScenarioFailure)) {
        throw error;
      }

      outcomes.set(id, error.message);
    }
  };

  const first = await startHost(dll, variables);
  const recorded = {};
  try {
    await attempt('several-commands', async () => {
      const { traceId, body } = await callAndGetTraceId(first.app, `/db/${kind}/several-commands`, 200);
      recorded.several = traceId;
      checkSeveralCommands(await untilTraceStored(first.app, traceId, SEVERAL_COMMANDS_SPAN_COUNT), kind, body);
    });

    const failing = await callAndGetTraceId(first.app, `/db/${kind}/fails`, 500).catch((error) => ({ error }));
    recorded.failing = failing.traceId;
    if (recorded.failing) {
      await untilTraceStored(first.app, recorded.failing, 2).catch(() => undefined);
    }

    await attempt('filters', async () => {
      expect(Boolean(recorded.several) && Boolean(recorded.failing), failing.error?.message ?? 'the traces to filter were not recorded');
      await untilListed(first.app, [recorded.several, recorded.failing]);
      const byRoute = await listTraces(first.app, `&route=${encodeURIComponent(`/db/${kind}/fails`)}`);
      expect(
        byRoute.length === 1 && byRoute[0].traceId === recorded.failing,
        `filtering by the route of the failing endpoint listed [${byRoute.map((item) => item.routeTemplate).join(', ')}], expected only the failing trace`,
      );
      const byError = await listTraces(first.app, '&status=Error');
      expect(
        byError.length === 1 && byError[0].traceId === recorded.failing,
        `filtering by status Error listed ${byError.length} traces, expected only the failing one`,
      );
      const bySuccess = await listTraces(first.app, '&status=Success');
      expect(
        bySuccess.length === 1 && bySuccess[0].traceId === recorded.several,
        `filtering by status Success listed ${bySuccess.length} traces, expected only the one with the commands`,
      );
    });
  } finally {
    await first.application.stop();
  }

  const second = await startHost(dll, variables);
  try {
    await attempt('restart', async () => {
      expect(Boolean(recorded.several) && Boolean(recorded.failing), 'the traces to find again were not recorded');
      const listed = (await listTraces(second.app)).map((item) => item.traceId);
      for (const traceId of [recorded.several, recorded.failing]) {
        expect(listed.includes(traceId), `the trace ${traceId} is not listed after the restart, the list has ${listed.length} traces`);
      }

      const detail = await untilTraceStored(second.app, recorded.several, SEVERAL_COMMANDS_SPAN_COUNT);
      expect(commandsOf(detail, kind).length === EXPECTED_COMMANDS.length, 'the steps of the trace did not come back after the restart');
    });
  } finally {
    await second.application.stop();
  }

  return outcomes;
}

async function runSchemaScript(context) {
  const { kind, dll, connection } = context;
  const variables = variablesFor(kind, connection, `s${randomBytes(4).toString('hex')}_`, {
    DATABASE_SCHEMA_SQL: KINDS[kind].schemaSql,
    DATABASE_AUTO_CREATE_SCHEMA: 'false',
  });

  const host = await startHost(dll, variables);
  try {
    const { traceId } = await callAndGetTraceId(host.app, `/db/${kind}/several-commands`, 200);
    await untilTraceStored(host.app, traceId, SEVERAL_COMMANDS_SPAN_COUNT);
  } finally {
    await host.application.stop();
  }
}

export async function runBuild(label, dll, connections) {
  const failures = [];
  for (const kind of Object.keys(KINDS)) {
    const context = { kind, dll, connection: connections[kind] };
    const outcomes = await runSession(context);
    if (label === 'new') {
      try {
        await runSchemaScript(context);
        outcomes.set('schema-script', undefined);
      } catch (error) {
        outcomes.set('schema-script', error instanceof ScenarioFailure ? error.message : `${error.message}`);
      }
    }

    for (const scenario of SCENARIOS.filter((candidate) => candidate.onlyFor === undefined || candidate.onlyFor === label)) {
      const failure = outcomes.get(scenario.id);
      if (failure === undefined) {
        console.log(`PASS database:${kind}:${scenario.id} [${label}] ${scenario.title}`);
      } else {
        failures.push(`database:${kind}:${scenario.id} [${label}]`);
        console.error(`FAIL database:${kind}:${scenario.id} [${label}] ${scenario.title}\n     ${failure}`);
      }
    }
  }

  return failures;
}

function parseArguments(argumentList) {
  const options = {};
  for (let index = 0; index < argumentList.length; index += 2) {
    const name = argumentList[index];
    const value = argumentList[index + 1];
    if (['--new-dll', '--old-dll', '--postgres', '--sqlserver'].includes(name)) {
      options[name.slice(2)] = value;
    } else {
      throw new Error(`Unknown argument "${name}". Use --new-dll <DatabaseApp.dll> --old-dll <DatabaseApp.dll> --postgres <connection string> --sqlserver <connection string>.`);
    }
  }

  for (const required of ['new-dll', 'old-dll', 'postgres', 'sqlserver']) {
    if (!options[required]) {
      throw new Error(`Pass --${required}.`);
    }
  }

  return options;
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const connections = { postgres: options.postgres, sqlserver: options.sqlserver };
  const failures = [
    ...(await runBuild('new', options['new-dll'], connections)),
    ...(await runBuild('old', options['old-dll'], connections)),
  ];

  console.log(`\nSUMMARY scenarios=${CHECK_IDS.length * 2 - Object.keys(KINDS).length} failed=${failures.length}`);
  process.exit(failures.length > 0 ? 1 : 0);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  await main();
}
