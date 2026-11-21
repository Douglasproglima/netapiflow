import { spawn } from 'node:child_process';
import { access, mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { delimiter, join } from 'node:path';

const DEBUGGER_START_TIMEOUT_MS = 30_000;
const COMMAND_TIMEOUT_MS = 30_000;
const PROFILE_REMOVE_ATTEMPTS = 10;
const PROFILE_REMOVE_DELAY_MS = 300;

const LINUX_BROWSERS = ['google-chrome', 'google-chrome-stable', 'chromium', 'chromium-browser'];
const FIXED_BROWSER_PATHS = [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
];

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

async function exists(path) {
  return access(path).then(
    () => true,
    () => false,
  );
}

export async function findBrowser() {
  if (process.env.CHROME_PATH) {
    return process.env.CHROME_PATH;
  }

  for (const directory of (process.env.PATH ?? '').split(delimiter)) {
    for (const name of LINUX_BROWSERS) {
      const candidate = join(directory, name);
      if (await exists(candidate)) {
        return candidate;
      }
    }
  }

  for (const candidate of FIXED_BROWSER_PATHS) {
    if (await exists(candidate)) {
      return candidate;
    }
  }

  throw new Error('No Chrome or Edge was found. Set CHROME_PATH to the browser to use.');
}

async function untilDebuggerAnswers(port) {
  const deadline = Date.now() + DEBUGGER_START_TIMEOUT_MS;
  while (Date.now() < deadline) {
    const tabs = await fetch(`http://127.0.0.1:${port}/json`).then(
      (response) => (response.ok ? response.json() : undefined),
      () => undefined,
    );
    const page = tabs?.find((tab) => tab.type === 'page');
    if (page) {
      return page.webSocketDebuggerUrl;
    }

    await delay(250);
  }

  throw new Error(`The browser did not open its debugging port ${port} in ${DEBUGGER_START_TIMEOUT_MS} ms.`);
}

function connect(url) {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(url);
    socket.onopen = () => resolve(socket);
    socket.onerror = () => reject(new Error(`Could not connect to ${url}.`));
  });
}

export async function openBrowser({ executablePath, port, profileDirectory, sandbox = false }) {
  const ownsProfile = profileDirectory === undefined;
  const directory = profileDirectory ?? (await mkdtemp(join(tmpdir(), 'netapiflow-browser-')));
  const flags = [
    '--headless=new',
    `--remote-debugging-port=${port}`,
    `--user-data-dir=${directory}`,
    '--no-first-run',
    '--disable-extensions',
    '--disable-gpu',
    ...(sandbox ? [] : ['--no-sandbox']),
    'about:blank',
  ];
  const child = spawn(executablePath, flags, { stdio: 'ignore' });
  let exited = false;
  child.on('exit', () => {
    exited = true;
  });

  let socket;
  try {
    socket = await connect(await untilDebuggerAnswers(port));
  } catch (error) {
    child.kill();
    throw error;
  }

  let nextId = 0;
  const pending = new Map();
  const listeners = [];
  socket.onmessage = (event) => {
    const message = JSON.parse(event.data);
    if (message.id !== undefined && pending.has(message.id)) {
      pending.get(message.id)(message);
    } else if (message.method) {
      listeners.forEach((listener) => listener(message));
    }
  };

  const send = (method, params = {}) =>
    new Promise((resolve, reject) => {
      const id = ++nextId;
      const timer = setTimeout(() => reject(new Error(`${method} did not answer in ${COMMAND_TIMEOUT_MS} ms.`)), COMMAND_TIMEOUT_MS);
      pending.set(id, (message) => {
        clearTimeout(timer);
        pending.delete(id);
        if (message.error) {
          reject(new Error(`${method}: ${message.error.message}`));
        } else {
          resolve(message.result);
        }
      });
      socket.send(JSON.stringify({ id, method, params }));
    });

  return {
    send,
    onEvent: (listener) => listeners.push(listener),
    evaluate: async (expression) => (await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true })).result?.value,
    close: async () => {
      try {
        socket.close();
      } catch {
        socket = undefined;
      }

      if (!exited) {
        child.kill();
        await new Promise((resolve) => {
          child.once('exit', resolve);
          setTimeout(resolve, 5_000);
        });
      }

      if (ownsProfile) {
        await rm(directory, { recursive: true, force: true, maxRetries: PROFILE_REMOVE_ATTEMPTS, retryDelay: PROFILE_REMOVE_DELAY_MS });
      }
    },
  };
}
