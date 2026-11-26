async (page) => {
  const outDir = 'C:\\WP\\Estudos\\netapiflow\\.claude\\worktrees\\fase-60a\\tests\\manual-verification\\Fase60AcabamentoPainelVerification\\';
  page.setDefaultTimeout(30000);
  const slowTrace = 'f7cebccd013bd8b285cb6fa88339265a';
  const builds = [
    { stage: 'antes', base: 'http://localhost:4874' },
    { stage: 'depois', base: 'http://localhost:4872' },
  ];
  const results = {};

  for (const { stage, base } of builds) {
    await page.emulateMedia({ colorScheme: 'dark' });
    await page.goto(base + '/');
    await page.evaluate(() => localStorage.setItem('naf-theme', 'dark'));
    await page.setViewportSize({ width: 1440, height: 900 });

    await page.goto(`${base}/?trace=${slowTrace}`);
    await page.waitForSelector('.flow-workspace__banner');
    await page.waitForTimeout(800);
    const flow = await page.evaluate(() => {
      const root = document.querySelector('.flow-details');
      return {
        banner: document.querySelector('.flow-workspace__banner').textContent.replace(/\s+/g, ' ').trim(),
        unexplained: document.querySelector('.flow-details__unexplained')?.textContent.trim() ?? null,
        summaryLabel: root.getAttribute('aria-label'),
        routeTitles: [document.querySelector('.flow-workspace__route')?.getAttribute('title'), document.querySelector('.flow-details__route')?.getAttribute('title')],
        waterfallRowsAreButtons: document.querySelectorAll('.flow-waterfall__row button').length,
      };
    });
    await page.screenshot({ path: `${outDir}${stage}-60-2-fluxo-1440.png` });

    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`${base}/`);
    await page.waitForSelector('.app-topbar__right');
    await page.waitForTimeout(600);
    const topbar = await page.evaluate(() => {
      const button = document.querySelector('.app-topbar__try-endpoint');
      return { present: !!button, ariaLabel: button?.getAttribute('aria-label') ?? null, visibleText: button ? button.innerText.trim() : null };
    });

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(base + '/runner');
    await page.waitForSelector('.endpoint-form__endpoint');
    await page.click('[title="GET /api/orders/{id}"]');
    await page.waitForSelector('#endpoint-form-field-id');
    const labelBefore = await page.evaluate(() => document.querySelector('.endpoint-form__field label').textContent);
    await page.fill('#endpoint-form-field-id', '5');
    await page.click('.endpoint-form__send');
    await page.waitForTimeout(1500);
    await page.evaluate(() => { const details = document.querySelector('.endpoint-form__response-body'); if (details) details.open = true; });
    await page.waitForTimeout(300);
    const runner = await page.evaluate(() => ({
      label: document.querySelector('.endpoint-form__field label').textContent,
      status: document.querySelector('.endpoint-form__response-status')?.textContent.trim() ?? null,
      body: document.querySelector('.endpoint-form__response-text')?.textContent.slice(0, 50) ?? null,
      traceIsLink: !!document.querySelector('.endpoint-form__trace-id a'),
      traceText: document.querySelector('.endpoint-form__trace-id')?.textContent.trim().slice(0, 30) ?? null,
    }));
    await page.screenshot({ path: `${outDir}${stage}-60-3-runner-demo-1440.png` });
    results[stage] = { flow, topbar, runner: { labelBefore, ...runner } };
  }
  return results;
}
