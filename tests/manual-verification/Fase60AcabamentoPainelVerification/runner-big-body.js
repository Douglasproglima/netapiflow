async (page) => {
  page.setDefaultTimeout(60000);
  const base = 'http://localhost:4873';
  await page.route('**/*', async (route) => {
    const path = new URL(route.request().url()).pathname;
    const json = (body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
    if (path === '/api/config') return json({ slowThresholdMicros: 250000, version: '0.6.0', tailSamplingEnabled: false, successSampleRate: 1, openApiDocumentPath: '/openapi/v1.json', hostPathBase: '' });
    if (path === '/api/traces/live') return json([]);
    if (path === '/openapi/v1.json') return json({ paths: { '/big': { get: { operationId: 'Big' } } } });
    return route.continue();
  });

  const out = {};
  for (const [label, megabytes] of [['1 MB', 1], ['5 MB', 5], ['5 MB again', 5]]) {
    await page.goto(base + '/runner');
    await page.waitForSelector('.endpoint-form__endpoint');
    await page.route('**/big', (route) =>
      route.fulfill({ status: 200, contentType: 'text/plain', headers: { 'X-NetApiFlow-TraceId': '0123456789abcdef0123456789abcdef' }, body: 'x'.repeat(megabytes * 1024 * 1024) }),
    );
    await page.click('[title="GET /big"]');
    await page.waitForTimeout(300);
    await page.evaluate(() => {
      window.__longTasks = [];
      new PerformanceObserver((list) => { for (const entry of list.getEntries()) window.__longTasks.push(Math.round(entry.duration)); }).observe({ entryTypes: ['longtask'] });
    });
    await page.click('.endpoint-form__send');
    await page.waitForSelector('.endpoint-form__response');
    out[label] = await page.evaluate(async () => {
      await new Promise((resolve) => setTimeout(resolve, 400));
      return {
        longTasksMs: window.__longTasks,
        heapMb: performance.memory ? Math.round(performance.memory.usedJSHeapSize / 1048576) : null,
        renderedChars: document.querySelector('.endpoint-form__response-text').textContent.length,
      };
    });
    await page.unroute('**/big');
  }
  return out;
}
