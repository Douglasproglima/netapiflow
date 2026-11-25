async (page) => {
  const stage = 'depois';
  const outDir = 'tests/manual-verification/Fase68AcessibilidadeVerification/';
  const base = 'http://localhost:4871';
  const combos = [
    { name: 'desktop-escuro', theme: 'dark', width: 1440, height: 900, shoot: true },
    { name: 'desktop-claro', theme: 'light', width: 1440, height: 900, shoot: false },
    { name: '390x844-escuro', theme: 'dark', width: 390, height: 844, shoot: false },
    { name: '390x844-claro', theme: 'light', width: 390, height: 844, shoot: true },
  ];

  const describe = () =>
    page.evaluate(() => {
      const a = document.activeElement;
      if (!a || a === document.body) return 'BODY';
      const label = (a.getAttribute('aria-label') || a.id || a.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 30);
      return a.tagName.toLowerCase() + '.' + String(a.className || '').split(' ')[0] + ' [' + label + ']';
    });

  const insideDialog = () =>
    page.evaluate(() => {
      const container = document.querySelector('[role=dialog]') || document.querySelector('app-endpoint-form');
      const a = document.activeElement;
      return !!(container && a && container.contains(a));
    });

  const tabUntil = async (predicate, max = 40) => {
    for (let i = 1; i <= max; i++) {
      await page.keyboard.press('Tab');
      if (predicate(await describe())) return i;
    }
    return -1;
  };

  const loadPage = async (path, ready) => {
    await page.goto(base + path);
    await page.waitForSelector(ready);
    await page.waitForTimeout(900);
    await page.evaluate(() => document.activeElement && document.activeElement.blur());
  };

  const results = {};
  for (const combo of combos) {
    const r = {};
    await page.emulateMedia({ colorScheme: combo.theme });
    await page.goto(base + '/');
    await page.evaluate((t) => localStorage.setItem('naf-theme', t), combo.theme);
    await page.setViewportSize({ width: combo.width, height: combo.height });

    await loadPage('/', '.app-topbar__try-endpoint');
    await tabUntil((d) => d.includes('app-topbar__try-endpoint'));
    await page.keyboard.press('Enter');
    await page.waitForTimeout(600);
    r.drawerAfterOpen = { focus: await describe(), insideDialog: await insideDialog() };
    r.drawerRoles = await page.evaluate(() => {
      const dialog = document.querySelector('[role=dialog]');
      const host = document.querySelector('app-endpoint-form');
      const node = dialog || host;
      return {
        roleDialogInDom: !!dialog,
        ariaModal: node.getAttribute('aria-modal'),
        accessibleName: node.getAttribute('aria-label') || (node.getAttribute('aria-labelledby') && document.getElementById(node.getAttribute('aria-labelledby'))?.textContent.trim()) || null,
      };
    });
    if (combo.shoot) await page.screenshot({ path: outDir + stage + '-b13b-gaveta-aberta-' + combo.name + '.png' });
    const tabSequence = [];
    for (let i = 0; i < 24; i++) {
      await page.keyboard.press('Tab');
      tabSequence.push(await insideDialog());
    }
    r.drawerTabOutOfDialog = tabSequence.some((inside) => !inside);
    r.drawerTabsOutsideOf24 = tabSequence.filter((inside) => !inside).length;
    await page.keyboard.press('Escape');
    await page.waitForTimeout(500);
    r.drawerOpenAfterEscape = await page.evaluate(() => document.querySelector('app-endpoint-form').classList.contains('flow-workspace__action--open'));
    r.drawerFocusAfterEscape = await describe();
    if (r.drawerOpenAfterEscape) {
      await page.evaluate(() => document.querySelector('.flow-workspace__action-close').focus());
      await page.keyboard.press('Enter');
      await page.waitForTimeout(500);
    }
    r.drawerFocusAfterClose = await describe();

    await page.evaluate(() => {
      const trigger = document.querySelector('.app-topbar__try-endpoint');
      if (!document.querySelector('app-endpoint-form').classList.contains('flow-workspace__action--open')) trigger.focus();
    });
    if (!(await page.evaluate(() => document.querySelector('app-endpoint-form').classList.contains('flow-workspace__action--open')))) {
      await page.keyboard.press('Enter');
      await page.waitForTimeout(500);
    }
    await page.evaluate(() => document.querySelector('.flow-workspace__action-close, .endpoint-form__close').focus());
    await page.keyboard.press('Enter');
    await page.waitForTimeout(500);
    r.drawerFocusAfterCloseButton = await describe();

    await loadPage('/runner', '.endpoint-form__endpoint');
    r.runnerTabsToFirstEndpoint = await tabUntil((d) => d.includes('endpoint-form__endpoint'));
    await page.keyboard.press('Enter');
    await page.waitForTimeout(500);
    r.runnerFocusAfterSelect = await describe();
    if (combo.shoot) await page.screenshot({ path: outDir + stage + '-b13c-runner-apos-escolher-' + combo.name + '.png' });
    const afterSelect = [];
    for (let i = 0; i < 4; i++) {
      await page.keyboard.press('Tab');
      afterSelect.push(await describe());
    }
    r.runnerTabSequenceAfterSelect = afterSelect;
    r.runnerIdFieldFocusedAfterSelect = r.runnerFocusAfterSelect.includes('endpoint-form-field-id');
    r.runnerSendReachedBeforeIdField = afterSelect[0].includes('Send request');

    await page.evaluate(() => document.querySelector('.endpoint-form__back').focus());
    await page.keyboard.press('Enter');
    await page.waitForTimeout(400);
    r.runnerFocusAfterBackToList = await describe();

    await loadPage('/runner', '.endpoint-form__endpoint');
    await page.evaluate(() => document.querySelector('.endpoint-form__endpoint').focus());
    await page.keyboard.press('Enter');
    await page.waitForTimeout(400);
    await page.evaluate(() => document.querySelector('.endpoint-form__headers-add').focus());
    await page.keyboard.press('Enter');
    await page.waitForTimeout(400);
    await page.evaluate(() => document.querySelector('.endpoint-form__header-remove').focus());
    await page.keyboard.press('Enter');
    await page.waitForTimeout(400);
    r.runnerFocusAfterRemoveHeader = await describe();

    await loadPage('/history', '.app-topbar');
    await page.waitForTimeout(600);
    const sortButtonVisible = await page.evaluate(() => {
      const button = document.querySelector('.naf-sort-btn');
      return !!button && button.offsetParent !== null;
    });
    if (sortButtonVisible) {
      await page.evaluate(() => document.querySelector('.naf-sort-btn').focus());
      await page.keyboard.press('Enter');
      await page.waitForTimeout(500);
      r.historyFocusAfterSort = await describe();
    } else {
      r.historyFocusAfterSort = 'sort buttons not rendered at this width';
    }

    results[combo.name] = r;
  }
  return results;
}
