import { spawn, spawnSync } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';

const [clientAppDir, label, scratchDir] = process.argv.slice(2);
const port = 9333;
const host = 'localhost:4871';
const edge = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const traceId = '571b83462c505f154553babd02d9923f';

const screens = [
  { name: 'flow', path: `/?trace=${traceId}` },
  { name: 'flow-gaveta', path: '/', click: '.app-topbar__try-endpoint' },
  { name: 'history', path: '/history' },
  { name: 'components', path: '/components' },
  { name: 'runner', path: '/runner' },
];
const combos = [
  { name: 'desktop-escuro', dark: true, width: 1440, height: 900 },
  { name: 'desktop-claro', dark: false, width: 1440, height: 900 },
  { name: '390x844-escuro', dark: true, width: 390, height: 844 },
  { name: '390x844-claro', dark: false, width: 390, height: 844 },
];

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function waitForDebugger() {
  for (let attempt = 0; attempt < 60; attempt++) {
    try {
      const response = await fetch(`http://127.0.0.1:${port}/json/version`);
      if (response.ok) return;
    } catch {}
    await sleep(500);
  }
  throw new Error('Edge debugging port never answered');
}

function cdpSession(webSocketUrl) {
  const socket = new WebSocket(webSocketUrl);
  let nextId = 1;
  const pending = new Map();
  socket.onmessage = (event) => {
    const message = JSON.parse(event.data);
    if (message.id && pending.has(message.id)) {
      pending.get(message.id)(message);
      pending.delete(message.id);
    }
  };
  const opened = new Promise((resolve) => (socket.onopen = resolve));
  const send = (method, params = {}) =>
    new Promise((resolve) => {
      const id = nextId++;
      pending.set(id, resolve);
      socket.send(JSON.stringify({ id, method, params }));
    });
  return { opened, send, close: () => socket.close() };
}

async function evaluate(session, expression) {
  const response = await session.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  return response.result?.result?.value;
}

const profileDir = join(scratchDir, 'edge-profile-' + label + '-' + Date.now());
mkdirSync(profileDir, { recursive: true });
const browser = spawn(edge, ['--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profileDir}`, '--no-first-run', '--disable-extensions', 'about:blank'], { stdio: 'ignore' });

const report = [];
try {
  await waitForDebugger();
  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  const page = targets.find((target) => target.type === 'page');
  const session = cdpSession(page.webSocketDebuggerUrl);
  await session.opened;
  await session.send('Page.enable');
  await session.send('Runtime.enable');

  for (const combo of combos) {
    await session.send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: combo.dark ? 'dark' : 'light' }] });
    await session.send('Emulation.setDeviceMetricsOverride', { width: combo.width, height: combo.height, deviceScaleFactor: 1, mobile: false });
    for (const screen of screens) {
      await session.send('Page.navigate', { url: `http://${host}${screen.path}` });
      let rendered = 0;
      for (let attempt = 0; attempt < 40 && rendered < 3000; attempt++) {
        await sleep(500);
        rendered = (await evaluate(session, `document.querySelector('app-root')?.innerHTML.length ?? 0`)) ?? 0;
      }
      await sleep(1200);
      if (screen.click) {
        const clicked = await evaluate(session, `(() => { const el = document.querySelector(${JSON.stringify(screen.click)}); if (!el) return false; el.click(); return true; })()`);
        if (!clicked) {
          report.push({ screen: screen.name, combo: combo.name, error: 'click target missing at this width' });
          continue;
        }
        await sleep(900);
      }
      const probe = await evaluate(session, `({ width: innerWidth, dark: matchMedia('(prefers-color-scheme: dark)').matches, bg: getComputedStyle(document.body).backgroundColor, nodes: document.querySelectorAll('*').length, drawerOpen: document.querySelector('app-endpoint-form')?.classList.contains('flow-workspace__action--open') ?? null })`);
      const run = spawnSync('npx', ['impeccable', 'detect', '--json', '--cdp', String(port), '--target', `${host}${screen.path}`], { cwd: clientAppDir, encoding: 'utf8', shell: true, timeout: 180000 });
      let findings = null;
      try {
        findings = JSON.parse(run.stdout);
      } catch {
        findings = { unparsed: (run.stdout || '').slice(0, 300), stderr: (run.stderr || '').slice(0, 300) };
      }
      const list = Array.isArray(findings) ? findings : null;
      const byRule = {};
      if (list) for (const finding of list) byRule[finding.antipattern ?? finding.rule ?? finding.id ?? 'unknown'] = (byRule[finding.antipattern ?? finding.rule ?? finding.id ?? 'unknown'] ?? 0) + 1;
      report.push({ screen: screen.name, combo: combo.name, probe, total: list ? list.length : null, byRule, raw: list ? undefined : findings, sample: list ? list.slice(0, 2) : undefined });
    }
  }
  session.close();
} finally {
  browser.kill();
  spawnSync('powershell', ['-NoProfile', '-Command', `Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*${profileDir.replace(/\\/g, '\\\\')}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }`], { stdio: 'ignore' });
  await sleep(1500);
}

console.log(JSON.stringify({ label, report }, null, 1));
