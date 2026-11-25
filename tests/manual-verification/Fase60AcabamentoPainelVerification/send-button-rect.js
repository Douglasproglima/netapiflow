async (page) => {
  const stage = 'depois';
  const outDir = 'C:\\WP\\Estudos\\netapiflow\\.claude\\worktrees\\fase-60a\\tests\\manual-verification\\Fase60AcabamentoPainelVerification\\';
  const base = 'http://localhost:4872';

  await page.route('**/assets/demo-fixtures/openapi-document.json', async (route) => {
    const response = await route.fetch();
    const document = await response.json();
    document.paths['/api/orders'] = {
      post: {
        operationId: 'CreateOrder',
        requestBody: { required: true, content: { 'application/json': { example: { customerId: 7, items: [{ sku: 'A-1', quantity: 2 }] } } } },
      },
    };
    document.paths['/api/orders/{id}/notes'] = {
      post: {
        operationId: 'AddNote',
        parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'integer' } }],
        requestBody: { required: true, content: { 'application/json': { schema: { type: 'object' } } } },
      },
    };
    await route.fulfill({ response, json: document });
  });

  const combos = [
    { width: 390, height: 844 },
    { width: 768, height: 1024 },
    { width: 1023, height: 768 },
  ];
  const endpoints = [
    { name: 'get', title: 'GET /api/orders/{id}' },
    { name: 'post', title: 'POST /api/orders' },
    { name: 'post-com-parametro', title: 'POST /api/orders/{id}/notes' },
  ];
  const results = [];

  for (const theme of ['dark', 'light']) {
    await page.emulateMedia({ colorScheme: theme });
    await page.goto(base + '/');
    await page.evaluate((value) => localStorage.setItem('naf-theme', value), theme);
    for (const { width, height } of combos) {
      await page.setViewportSize({ width, height });
      for (const endpoint of endpoints) {
        await page.goto(base + '/');
        await page.waitForSelector('.app-topbar__try-endpoint');
        await page.waitForTimeout(700);
        await page.click('.app-topbar__try-endpoint');
        await page.waitForSelector(`[title="${endpoint.title}"]`, { state: 'visible' });
        await page.click(`[title="${endpoint.title}"]`);
        await page.waitForTimeout(500);
        const measure = () =>
          page.evaluate(() => {
            const send = document.querySelector('.endpoint-form__send');
            const drawer = document.querySelector('app-endpoint-form');
            const r = send.getBoundingClientRect();
            const d = drawer.getBoundingClientRect();
            const visible = Math.max(0, Math.min(r.bottom, innerHeight) - Math.max(r.top, 0));
            return {
              innerHeight,
              send: { top: Math.round(r.top), bottom: Math.round(r.bottom), height: Math.round(r.height) },
              drawer: { top: Math.round(d.top), bottom: Math.round(d.bottom) },
              visiblePx: Math.round(visible),
              fullyVisible: r.top >= 0 && r.bottom <= innerHeight,
              documentScrollTop: document.scrollingElement.scrollTop,
            };
          });
        const rest = await measure();
        await page.evaluate(() => { const scroller = document.querySelector('.endpoint-form__custom'); if (scroller) scroller.scrollTop = scroller.scrollHeight; });
        await page.waitForTimeout(200);
        const afterDrawerScroll = await measure();
        await page.keyboard.press('Tab');
        await page.evaluate(() => document.querySelector('.endpoint-form__send').focus());
        await page.waitForTimeout(300);
        const afterTab = await measure();
        await page.screenshot({ path: outDir + stage + '-60-12-' + endpoint.name + '-' + width + '-' + theme + '.png' });
        results.push({ theme, width, endpoint: endpoint.name, rest, afterDrawerScroll, afterTab });
      }
    }
  }
  return results.map((r) => ({
    theme: r.theme,
    width: r.width,
    endpoint: r.endpoint,
    innerHeight: r.rest.innerHeight,
    drawerBottom: r.rest.drawer.bottom,
    send: `${r.rest.send.top}-${r.rest.send.bottom}`,
    visiblePx: r.rest.visiblePx,
    fullyVisible: r.rest.fullyVisible,
    afterDrawerScroll: `${r.afterDrawerScroll.send.top}-${r.afterDrawerScroll.send.bottom} visible ${r.afterDrawerScroll.visiblePx}`,
    afterTab: `${r.afterTab.send.top}-${r.afterTab.send.bottom} visible ${r.afterTab.visiblePx} docScroll ${r.afterTab.documentScrollTop}`,
  }));
}
