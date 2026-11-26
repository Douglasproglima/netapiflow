async (page) => {
  const stage = 'depois';
  const outDir = 'C:\\WP\\Estudos\\netapiflow\\.claude\\worktrees\\fase-60a\\tests\\manual-verification\\Fase60AcabamentoPainelVerification\\';
  const base = 'http://localhost:4873';
  page.setDefaultTimeout(30000);

  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    const json = (body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
    if (path.endsWith('/api/config')) return json({ slowThresholdMicros: 250000, version: '0.6.0', tailSamplingEnabled: false, successSampleRate: 1, openApiDocumentPath: '/openapi/v1.json', hostPathBase: '' });
    if (path.endsWith('/api/traces/live')) return json([]);
    if (path.endsWith('/api/endpoints')) return json([{ method: 'GET', routeTemplate: '/api/orders/{id}' }]);
    if (path.endsWith('/api/architecture')) {
      return json({
        totalExecutions: 3,
        nodes: [
          { component: 'OrdersController', operation: 'Get', type: 'Controller', executionCount: 3 },
          { component: 'OrderService', operation: 'LoadAsync', type: 'Service', executionCount: 3 },
          { component: 'OrderRepository', operation: 'GetItems', type: 'Repository', executionCount: 3 },
          { component: 'Database', operation: 'SELECT items', type: 'Database', executionCount: 15 },
          { component: 'Cache', operation: 'TryGet', type: 'Custom', executionCount: 1 },
        ],
        edges: [
          { fromComponent: 'OrdersController', fromOperation: 'Get', toComponent: 'OrderService', toOperation: 'LoadAsync' },
          { fromComponent: 'OrderService', fromOperation: 'LoadAsync', toComponent: 'OrderRepository', toOperation: 'GetItems' },
          { fromComponent: 'OrderRepository', fromOperation: 'GetItems', toComponent: 'Database', toOperation: 'SELECT items' },
          { fromComponent: 'OrderService', fromOperation: 'LoadAsync', toComponent: 'Cache', toOperation: 'TryGet' },
        ],
      });
    }
    if (path.includes('/node-states/') || path.endsWith('.svg')) return route.continue();
    return json([]);
  });

  await page.emulateMedia({ colorScheme: 'dark' });
  await page.goto(base + '/');
  await page.evaluate(() => localStorage.setItem('naf-theme', 'dark'));
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(base + '/architecture');
  await page.waitForSelector('select');
  await page.waitForTimeout(800);
  await page.selectOption('select', 'GET /api/orders/{id}');
  await page.waitForSelector('.flow-graph__node');
  await page.waitForTimeout(1200);
  const out = {};
  out.screen = await page.evaluate(() => ({
    legend: [...document.querySelectorAll('.flow-graph__legend-item')].map((e) => e.textContent.trim()),
    nodes: [...document.querySelectorAll('.flow-graph__node')].map((n) => n.textContent.replace(/\s+/g, ' ').trim()),
  }));
  await page.screenshot({ path: outDir + stage + '-60-7-arquitetura-1440.png' });
  const downloadPromise = page.waitForEvent('download', { timeout: 20000 });
  await page.locator('.architecture__exports button').nth(1).click();
  const download = await downloadPromise;
  const svgPath = outDir + stage + '-60-7-exportado.svg';
  await download.saveAs(svgPath);
  let svg = '';
  for await (const chunk of await download.createReadStream()) svg += chunk;
  out.svg = { bytes: svg.length, ngcontent: (svg.match(/_ngcontent-[a-z0-9-]+/g) || []).length, styleTags: (svg.match(/<style/g) || []).length, classes: (svg.match(/ class="/g) || []).length, textElements: (svg.match(/<text/g) || []).length };
  const standalone = await page.context().newPage();
  await standalone.setViewportSize({ width: 1200, height: 700 });
  await standalone.goto('file:///' + svgPath.replace(/\\/g, '/'));
  await standalone.waitForTimeout(800);
  await standalone.screenshot({ path: outDir + stage + '-60-7-exportado-aberto-sozinho.png' });
  out.standaloneText = await standalone.evaluate(() => [...document.querySelectorAll('text')].slice(0, 4).map((t) => ({ text: t.textContent.slice(0, 20), fill: getComputedStyle(t).fill, fontSize: getComputedStyle(t).fontSize, fontFamily: getComputedStyle(t).fontFamily.slice(0, 30) })));
  await standalone.close();
  return out;
}
