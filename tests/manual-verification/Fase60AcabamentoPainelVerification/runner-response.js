async (page) => {
  const stage = 'depois';
  const outDir = 'C:\\WP\\Estudos\\netapiflow\\.claude\\worktrees\\fase-60a\\tests\\manual-verification\\Fase60AcabamentoPainelVerification\\';
  const base = 'http://localhost:4873';
  page.setDefaultTimeout(60000);

  const fiveMegabytes = 'x'.repeat(5 * 1024 * 1024);
  await page.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    const json = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (path === '/api/config') return json({ slowThresholdMicros: 250000, version: '0.6.0', tailSamplingEnabled: false, successSampleRate: 1, openApiDocumentPath: '/openapi/v1.json', hostPathBase: '' });
    if (path === '/api/traces/live') return json([]);
    if (path === '/openapi/v1.json') {
      return json({
        paths: {
          '/big': { get: { operationId: 'Big' } },
          '/json': { get: { operationId: 'Json' } },
          '/html': { get: { operationId: 'Html' } },
          '/binary': { get: { operationId: 'Binary' } },
          '/boom': { get: { operationId: 'Boom' } },
          '/empty': { get: { operationId: 'Empty' } },
          '/orders': { post: { operationId: 'Create', requestBody: { required: true, content: { 'application/json': { example: { customerId: 7, items: ['a'] } } } } } },
        },
      });
    }
    const traced = { 'X-NetApiFlow-TraceId': '0123456789abcdef0123456789abcdef', 'Set-Cookie': 'session=top-secret; HttpOnly' };
    if (path === '/big') return route.fulfill({ status: 200, contentType: 'text/plain', headers: traced, body: fiveMegabytes });
    if (path === '/json') return route.fulfill({ status: 200, contentType: 'application/json', headers: traced, body: JSON.stringify({ id: 5, status: 'Paid', items: [1, 2, 3] }) });
    if (path === '/html') return route.fulfill({ status: 200, contentType: 'text/html', headers: traced, body: '<img src=x onerror=alert(1)><b>bold</b>' });
    if (path === '/binary') return route.fulfill({ status: 200, contentType: 'application/octet-stream', headers: traced, body: Buffer.from([0, 1, 2, 3]) });
    if (path === '/boom') return route.fulfill({ status: 500, contentType: 'text/plain', headers: traced, body: 'Unhandled exception: NullReferenceException' });
    if (path === '/empty') return route.fulfill({ status: 204, headers: traced });
    if (path === '/orders' && route.request().method() === 'POST') return route.fulfill({ status: 201, contentType: 'application/json', headers: traced, body: JSON.stringify({ created: true }) });
    return route.continue();
  });

  await page.emulateMedia({ colorScheme: 'dark' });
  await page.goto(base + '/');
  await page.evaluate(() => localStorage.setItem('naf-theme', 'dark'));
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(base + '/runner');
  await page.waitForSelector('.endpoint-form__endpoint');

  const results = {};
  const openAndSend = async (title) => {
    await page.click(`[title="${title}"]`);
    await page.waitForTimeout(300);
    await page.click('.endpoint-form__send');
  };
  const read = () =>
    page.evaluate(() => ({
      status: document.querySelector('.endpoint-form__response-status')?.textContent.trim() ?? null,
      text: (document.querySelector('.endpoint-form__response-text')?.textContent ?? '').slice(0, 60),
      textLength: document.querySelector('.endpoint-form__response-text')?.textContent.length ?? 0,
      note: document.querySelector('.endpoint-form__response-note')?.textContent.trim() ?? null,
      traceLink: document.querySelector('.endpoint-form__trace-id a')?.getAttribute('href') ?? null,
      images: document.querySelectorAll('.endpoint-form__response img, .endpoint-form__response b').length,
      mentionsHeader: document.body.textContent.includes('top-secret'),
      sendVisible: (() => { const r = document.querySelector('.endpoint-form__send').getBoundingClientRect(); return r.top >= 0 && r.bottom <= innerHeight; })(),
    }));

  for (const [name, title] of [['json', 'GET /json'], ['html', 'GET /html'], ['binary', 'GET /binary'], ['boom', 'GET /boom'], ['empty', 'GET /empty']]) {
    await openAndSend(title);
    await page.waitForSelector('.endpoint-form__response');
    await page.evaluate(() => { const details = document.querySelector('.endpoint-form__response-body'); if (details) details.open = true; });
    await page.waitForTimeout(300);
    results[name] = await read();
    if (name === 'json' || name === 'boom') await page.screenshot({ path: `${outDir}${stage}-60-3-runner-${name}.png` });
    await page.click('.endpoint-form__back');
  }

  await page.click('[title="POST /orders"]');
  await page.waitForSelector('textarea');
  results.postExample = await page.evaluate(() => document.querySelector('textarea').value.replace(/\s+/g, ' '));
  await page.click('.endpoint-form__back');

  await page.click('[title="GET /big"]');
  await page.waitForTimeout(300);
  await page.evaluate(() => {
    window.__frameGaps = [];
    let last = performance.now();
    const tick = (now) => { window.__frameGaps.push(now - last); last = now; if (window.__watching) requestAnimationFrame(tick); };
    window.__watching = true;
    requestAnimationFrame(tick);
    window.__clickedAt = performance.now();
  });
  await page.click('.endpoint-form__send');
  await page.waitForSelector('.endpoint-form__response');
  const timing = await page.evaluate(() => {
    const elapsed = performance.now() - window.__clickedAt;
    window.__watching = false;
    return { elapsedMs: Math.round(elapsed), longestFrameGapMs: Math.round(Math.max(...window.__frameGaps)) };
  });
  await page.evaluate(() => { document.querySelector('.endpoint-form__response-body').open = true; });
  await page.waitForTimeout(300);
  results.big = { ...(await read()), ...timing, bodyBytes: 5 * 1024 * 1024 };
  await page.screenshot({ path: `${outDir}${stage}-60-3-runner-5mb.png` });
  return results;
}
