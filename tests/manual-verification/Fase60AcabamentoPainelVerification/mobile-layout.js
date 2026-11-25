async (page) => {
  const stage = 'depois';
  const outDir = 'C:\\WP\\Estudos\\netapiflow\\.claude\\worktrees\\fase-60a\\tests\\manual-verification\\Fase60AcabamentoPainelVerification\\';
  const base = 'http://localhost:4872';
  const traceA = '571b83462c505f154553babd02d9923f';
  const traceB = 'b82b555b8d280b7c9999fcbe55fc9eba';
  page.setDefaultTimeout(30000);

  const results = [];
  for (const theme of ['dark', 'light']) {
    await page.emulateMedia({ colorScheme: theme });
    await page.goto(base + '/');
    await page.evaluate((value) => localStorage.setItem('naf-theme', value), theme);
    await page.setViewportSize({ width: 390, height: 844 });

    await page.goto(`${base}/diff?a=${traceA}&b=${traceB}`);
    await page.waitForSelector('.diff-page__table');
    await page.waitForTimeout(800);
    const compare = await page.evaluate(() => {
      const scroll = document.querySelector('.diff-page__table-scroll');
      const cells = [...document.querySelectorAll('.diff-page__row')].slice(0, 3).map((row) => [...row.querySelectorAll('td')].map((td) => { const r = td.getBoundingClientRect(); return { label: td.getAttribute('data-label'), left: Math.round(r.left), right: Math.round(r.right), text: td.textContent.replace(/\s+/g, ' ').trim().slice(0, 40) }; }));
      return { innerWidth, scrollWidth: scroll.scrollWidth, clientWidth: scroll.clientWidth, documentScrollWidth: document.documentElement.scrollWidth, firstRows: cells };
    });
    await page.screenshot({ path: `${outDir}${stage}-60-1-comparar-390-${theme}.png` });

    await page.goto(base + '/insights');
    await page.waitForTimeout(1500);
    const insights = await page.evaluate(() => {
      const table = document.querySelector('.insights__table');
      const cells = [...table.querySelectorAll('tbody tr')].slice(0, 2).map((row) => [...row.querySelectorAll('td')].map((td) => { const r = td.getBoundingClientRect(); return { label: td.getAttribute('data-label'), left: Math.round(r.left), right: Math.round(r.right), text: td.textContent.replace(/\s+/g, ' ').trim().slice(0, 30) }; }));
      return { innerWidth, documentScrollWidth: document.documentElement.scrollWidth, blockScrollWidth: table.parentElement.scrollWidth, blockClientWidth: table.parentElement.clientWidth, firstRows: cells };
    });
    await page.screenshot({ path: `${outDir}${stage}-60-1-insights-390-${theme}.png` });

    await page.goto(base + '/history');
    await page.waitForTimeout(1500);
    const boxes = await page.$$('input[type=checkbox]');
    for (let i = 0; i < 2; i++) await boxes[i].check({ force: true });
    await page.waitForTimeout(500);
    const history = await page.evaluate(() => {
      const compareButton = [...document.querySelectorAll('button')].find((b) => /comparar|compare/i.test(b.textContent) && b.offsetParent);
      const r = compareButton.getBoundingClientRect();
      const routes = [...document.querySelectorAll('.history__row-link')].slice(0, 3).map((a) => ({ text: a.textContent.trim().slice(0, 40), scrollW: a.scrollWidth, clientW: a.clientWidth, truncated: a.scrollWidth > a.clientWidth + 1 }));
      return { innerWidth, documentScrollWidth: document.documentElement.scrollWidth, compareButton: { left: Math.round(r.left), right: Math.round(r.right), inside: r.left >= 0 && r.right <= innerWidth }, routes };
    });
    await page.screenshot({ path: `${outDir}${stage}-60-1-historico-390-${theme}.png` });
    results.push({ theme, compare, insights, history });
  }
  return results;
}
