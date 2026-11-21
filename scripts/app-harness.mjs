import { spawn } from 'node:child_process';
import { request as httpRequest } from 'node:http';
import { createServer } from 'node:net';
import { dirname } from 'node:path';

const READY_TIMEOUT_MS = 60_000;
const REQUEST_TIMEOUT_MS = 20_000;
const STOP_TIMEOUT_MS = 10_000;
const LOG_TAIL_LINES = 40;

export function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

export function send(base, { method = 'GET', path, headers = {}, body }) {
  return new Promise((resolve, reject) => {
    const outgoing = httpRequest(
      { hostname: base.hostname, port: base.port, method, path, headers: { connection: 'close', ...headers }, timeout: REQUEST_TIMEOUT_MS },
      (incoming) => {
        const chunks = [];
        incoming.on('data', (chunk) => chunks.push(chunk));
        incoming.on('end', () => resolve({ status: incoming.statusCode, headers: incoming.headers, text: Buffer.concat(chunks).toString('utf8') }));
        incoming.on('error', reject);
      },
    );
    outgoing.on('timeout', () => outgoing.destroy(new Error(`${method} ${path} did not answer in ${REQUEST_TIMEOUT_MS} ms`)));
    outgoing.on('error', reject);
    outgoing.end(body);
  });
}

export function clientFor(baseUrl) {
  const base = new URL(baseUrl);
  return {
    base,
    send: (options) => send(base, options),
    get: (path, headers = {}) => send(base, { path, headers }),
  };
}

export async function freePort() {
  return new Promise((resolve, reject) => {
    const probe = createServer();
    probe.on('error', reject);
    probe.listen(0, '127.0.0.1', () => {
      const { port } = probe.address();
      probe.close(() => resolve(port));
    });
  });
}

export function startApplication(dll, { environment, variables }, port) {
  const lines = [];
  let exitCode = null;
  const child = spawn('dotnet', [dll], {
    cwd: dirname(dll),
    env: {
      ...process.env,
      ...variables,
      ASPNETCORE_ENVIRONMENT: environment,
      ASPNETCORE_URLS: `http://127.0.0.1:${port}`,
      DOTNET_ENVIRONMENT: environment,
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  for (const stream of [child.stdout, child.stderr]) {
    stream.on('data', (chunk) => lines.push(...chunk.toString('utf8').split(/\r?\n/).filter(Boolean)));
  }

  child.on('exit', (code) => {
    exitCode = code ?? -1;
  });

  return {
    exited: () => exitCode !== null,
    logTail: () => lines.slice(-LOG_TAIL_LINES).join('\n'),
    stop: async () => {
      if (exitCode === null) {
        child.kill();
        await Promise.race([new Promise((resolve) => child.once('exit', resolve)), delay(STOP_TIMEOUT_MS)]);
      }

      if (exitCode === null) {
        child.kill('SIGKILL');
      }
    },
  };
}

export async function untilReady(app, application, readyPath = '/hello') {
  const deadline = Date.now() + READY_TIMEOUT_MS;
  while (Date.now() < deadline) {
    if (application?.exited()) {
      throw new Error(`The app exited before it was ready.\n${application.logTail()}`);
    }

    const answer = await app.get(readyPath).catch(() => undefined);
    if (answer?.status === 200) {
      return;
    }

    await delay(250);
  }

  throw new Error(`The app was not ready after ${READY_TIMEOUT_MS} ms.\n${application?.logTail() ?? ''}`);
}
