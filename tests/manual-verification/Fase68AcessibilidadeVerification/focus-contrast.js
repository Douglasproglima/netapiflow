async (page) => {
  const stage = 'depois';
  const outDir = 'tests/manual-verification/Fase68AcessibilidadeVerification/';
  const base = 'http://localhost:4871';

  const helpers = () => {
    const toRgba = (c) => {
      let m = c.match(/^rgba?\(([^)]+)\)$/);
      if (m) {
        const p = m[1].split(/[ ,\/]+/).filter(Boolean).map(Number);
        return { r: p[0], g: p[1], b: p[2], a: p[3] === undefined ? 1 : p[3] };
      }
      m = c.match(/^color\(srgb ([^)]+)\)$/);
      if (m) {
        const p = m[1].split(/[ \/]+/).filter(Boolean).map(Number);
        return { r: p[0] * 255, g: p[1] * 255, b: p[2] * 255, a: p[3] === undefined ? 1 : p[3] };
      }
      return { r: 0, g: 0, b: 0, a: 0 };
    };
    const over = (top, bottom) => {
      const a = top.a + bottom.a * (1 - top.a);
      return {
        r: (top.r * top.a + bottom.r * bottom.a * (1 - top.a)) / a,
        g: (top.g * top.a + bottom.g * bottom.a * (1 - top.a)) / a,
        b: (top.b * top.a + bottom.b * bottom.a * (1 - top.a)) / a,
        a,
      };
    };
    const linear = (v) => {
      v /= 255;
      return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
    };
    const luminance = (c) => 0.2126 * linear(c.r) + 0.7152 * linear(c.g) + 0.0722 * linear(c.b);
    const ratio = (x, y) => {
      const a = luminance(x);
      const b = luminance(y);
      return Math.round(((Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)) * 100) / 100;
    };
    const backgroundOf = (node) => {
      const stack = [];
      for (let n = node; n; n = n.parentElement) {
        const c = toRgba(getComputedStyle(n).backgroundColor);
        if (c.a > 0) stack.push(c);
        if (c.a === 1) break;
      }
      let result = { r: 255, g: 255, b: 255, a: 1 };
      if (stack.length && stack[stack.length - 1].a === 1) result = stack.pop();
      while (stack.length) result = over(stack.pop(), result);
      return result;
    };
    const rgb = (c) => [c.r, c.g, c.b].map(Math.round).join(',');
    window.__measureActive = () => {
      const el = document.activeElement;
      const style = getComputedStyle(el);
      const ownSurface = backgroundOf(el);
      const backdrop = backgroundOf(el.parentElement);
      const ripple = el.querySelector('.mat-mdc-button-persistent-ripple');
      let focusedSurface = ownSurface;
      let stateLayer = null;
      if (ripple) {
        const before = getComputedStyle(ripple, '::before');
        const color = toRgba(before.backgroundColor);
        const opacity = parseFloat(before.opacity);
        focusedSurface = over({ ...color, a: color.a * opacity }, ownSurface);
        stateLayer = { layer: rgb(color) + '@' + opacity, contrast: ratio(focusedSurface, ownSurface) };
      } else {
        const own = toRgba(style.backgroundColor);
        if (own.a > 0 && own.a < 1) {
          focusedSurface = over(own, backdrop);
          stateLayer = { layer: 'own ' + rgb(own) + '@' + own.a.toFixed(2), contrast: ratio(focusedSurface, backdrop) };
        }
      }
      const ringDrawn = style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) > 0;
      let ring = null;
      if (ringDrawn) {
        const color = toRgba(style.outlineColor);
        const offset = parseFloat(style.outlineOffset);
        const against = offset < 0 ? focusedSurface : backdrop;
        ring = { width: style.outlineWidth, offset: style.outlineOffset, color: rgb(color), against: rgb(against), contrast: ratio(over(color, against), against) };
      }
      const rect = el.getBoundingClientRect();
      return {
        tag: el.tagName.toLowerCase(),
        label: (el.getAttribute('aria-label') || el.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 26),
        focusVisible: el.matches(':focus-visible'),
        stateLayer,
        ring,
        indicatorContrast: ring ? ring.contrast : stateLayer ? stateLayer.contrast : null,
        rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height },
        dataTheme: document.documentElement.dataset.theme,
        focusToken: getComputedStyle(document.documentElement).getPropertyValue('--naf-focus').trim(),
      };
    };
  };
  await page.addInitScript(helpers);

  const results = [];
  const shoot = async (name, rect, pad = 16) => {
    const x = Math.max(0, rect.x - pad);
    const y = Math.max(0, rect.y - pad);
    await page.screenshot({ path: outDir + stage + '-b13a-' + name + '.png', clip: { x, y, width: rect.width + pad * 2, height: rect.height + pad * 2 } });
  };
  const measure = async (type, theme, setup, focusIt, pressTab = true) => {
    await setup();
    if (pressTab) await page.keyboard.press('Tab');
    await focusIt();
    await page.waitForTimeout(450);
    const measured = await page.evaluate(() => window.__measureActive());
    await shoot(type + '-' + theme, measured.rect);
    results.push({ type, theme, ...measured, rect: undefined });
  };

  for (const theme of ['dark', 'light']) {
    await page.emulateMedia({ colorScheme: theme });
    await page.goto(base + '/');
    await page.evaluate((value) => localStorage.setItem('naf-theme', value), theme);
    await page.setViewportSize({ width: 1440, height: 900 });
    const home = async () => {
      await page.goto(base + '/');
      await page.waitForSelector('.app-topbar');
      await page.waitForTimeout(900);
    };
    await measure('icon-button', theme, home, () => page.evaluate(() => document.querySelector('.app-topbar__theme--full').focus()));
    await measure('stroked-button', theme, home, () => page.evaluate(() => document.querySelector('.flow-workspace__toolbar button[mat-stroked-button]').focus()));
    await measure(
      'flat-button',
      theme,
      async () => {
        await page.goto(base + '/runner');
        await page.waitForSelector('.endpoint-form__endpoint');
        await page.waitForTimeout(600);
        await page.evaluate(() => document.querySelector('.endpoint-form__endpoint').focus());
        await page.keyboard.press('Enter');
        await page.waitForTimeout(500);
      },
      () => page.evaluate(() => document.querySelector('.endpoint-form__send').focus()),
    );
    await measure(
      'text-button',
      theme,
      async () => {
        await home();
        await page.click('button.app-rail__item');
        await page.waitForSelector('mat-dialog-actions button');
        await page.waitForTimeout(500);
      },
      () => page.evaluate(() => document.querySelector('mat-dialog-actions button').focus()),
    );
    await page.setViewportSize({ width: 390, height: 844 });
    await measure(
      'menu-item',
      theme,
      async () => {
        await home();
        await page.evaluate(() => document.querySelector('.app-topbar__prefs-trigger').focus());
        await page.keyboard.press('Enter');
        await page.waitForSelector('.mat-mdc-menu-item');
        await page.waitForTimeout(500);
      },
      async () => {},
      false,
    );
  }
  return results;
}
