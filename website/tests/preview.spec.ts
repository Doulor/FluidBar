import { expect, test } from '@playwright/test';

for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`real preview remains usable at ${viewport.width}px`, async ({ page }) => {
    await page.setViewportSize(viewport);
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    await page.goto('/');
    const frame = page.frameLocator('iframe[name="fluidbar-hero"]');
    await expect(frame.locator('body.ready')).toBeAttached();
    for (const [index, name] of ['主页', '媒体', '通知', '剪贴板', 'Agent', '小组件'].entries()) {
      const shortcut = page.getByRole('navigation', { name: '原型页面' }).getByRole('button', { name: new RegExp(name) });
      await shortcut.click();
      await expect(shortcut).toHaveAttribute('aria-pressed', 'true');
      await expect(frame.locator('.pager-title')).toContainText(name);
      await expect(frame.getByRole('tab', { name, exact: true })).toHaveAttribute('aria-selected', 'true');
      await expect(frame.locator('#pagerTrack > .page').nth(index)).toHaveAttribute('aria-hidden', 'false');
    }
    await frame.locator('#pagerCollapse').click();
    await expect(frame.locator('#island')).toHaveAttribute('data-expanded', 'false');
    await frame.locator('#island').click();
    await expect(frame.locator('#island')).toHaveAttribute('data-expanded', 'true');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await expect(page.locator('.noise--fixed, .noise-overlay, .stage-light, [data-ghost]')).toHaveCount(0);
    expect(errors).toEqual([]);
  });
}

for (const width of [1440, 390]) {
  test(`in-card sidebar switches all six pages and mirrors the website at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto('/');
    const frame = page.frameLocator('iframe[name="fluidbar-hero"]');
    const rail = frame.getByRole('tablist', { name: '页面' });
    await expect(rail).toBeVisible();
    await expect(rail.getByRole('tab')).toHaveCount(6);
    await expect(frame.locator('#miniRail')).toBeHidden();
    await expect.poll(() => frame.locator('#island').evaluate(el => [el.clientWidth, el.clientHeight])).toEqual([472, 284]);
    const geometry = await rail.evaluate(el => {
      const shell = document.querySelector('#island')!.getBoundingClientRect();
      const content = document.querySelector('#pager')!.getBoundingClientRect();
      return {
        tabs: Array.from(el.querySelectorAll('button')).map(tab => {
          const rect = tab.getBoundingClientRect();
          return [Math.round(rect.x - shell.x), Math.round(rect.y - shell.y), rect.width, rect.height];
        }),
        content: [Math.round(content.x - shell.x), Math.round(content.y - shell.y), content.width, content.height],
      };
    });
    expect(geometry.content).toEqual([60, 12, 400, 260]);
    expect(geometry.tabs).toEqual(Array.from({ length: 6 }, (_, index) => [10, 12 + 44 * index, 40, 40]));
    for (const name of ['小组件', '媒体', '主页', 'Agent', '通知', '剪贴板']) {
      await rail.getByRole('tab', { name, exact: true }).click();
      await expect(rail.getByRole('tab', { name, exact: true })).toHaveAttribute('aria-selected', 'true');
      await expect(frame.getByRole('tabpanel', { name, exact: true })).toBeVisible();
      await expect(page.locator('.hero-page-rail .is-active')).toContainText(name);
    }
    await frame.getByRole('tab', { name: '剪贴板', exact: true }).press('ArrowUp');
    await expect(frame.getByRole('tab', { name: '通知', exact: true })).toBeFocused();
    await expect(frame.getByRole('tab', { name: '通知', exact: true })).toHaveAttribute('aria-selected', 'true');
    await page.keyboard.press('End');
    await expect(frame.getByRole('tab', { name: '小组件', exact: true })).toHaveAttribute('aria-selected', 'true');
    await page.keyboard.press('Home');
    await expect(frame.getByRole('tab', { name: '主页', exact: true })).toHaveAttribute('aria-selected', 'true');
    await page.keyboard.press('Escape');
    await expect(rail).toBeHidden();
    await frame.locator('#island').click();
    await expect(rail).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

test('mobile material selector keeps the expanded translucent preview in view', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');
  const group = page.getByRole('group', { name: '岛材质' });
  const frame = page.frameLocator('iframe[name="fluidbar-lab"]');
  await group.scrollIntoViewIfNeeded();
  await expect(frame.locator('body.ready')).toBeAttached();
  for (const [name, value, maxAlpha] of [['黑曜石', 'obsidian', 1], ['亚克力', 'acrylic', 0.7], ['液态玻璃', 'liquid', 0.4]] as const) {
    await group.getByRole('button', { name: new RegExp(name) }).click();
    await expect(frame.locator('body')).toHaveAttribute('data-material', value);
    await expect(frame.locator('#island')).toHaveAttribute('data-expanded', 'true');
    await expect.poll(() => frame.locator('#island').evaluate(el => [el.clientWidth, el.clientHeight])).toEqual([472, 284]);
    const alpha = await frame.locator('#island').evaluate(el => {
      const color = getComputedStyle(el).backgroundColor;
      return color.startsWith('rgba') ? Number(color.split(',')[3].replace(')', '')) : 1;
    });
    expect(alpha).toBeLessThanOrEqual(maxAlpha);
    const bounds = await page.locator('.lab-stage').boundingBox();
    expect(bounds!.y).toBeGreaterThanOrEqual(72);
    expect(bounds!.y + bounds!.height).toBeLessThan(600);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
});

test('media lyrics advance through lines with continuous word fill and no audio', async ({ page }) => {
  const mediaRequests: string[] = [];
  page.on('request', request => {
    if (request.resourceType() === 'media' || /\.(mp3|wav|ogg|m4a)(\?|$)/i.test(request.url())) mediaRequests.push(request.url());
  });
  await page.goto('/');
  const frame = page.frameLocator('iframe[name="fluidbar-hero"]');
  await expect(frame.locator('body.ready')).toBeAttached();
  await page.getByRole('navigation', { name: '原型页面' }).getByRole('button', { name: /媒体/ }).click();
  const current = frame.locator('.lyric-line.is-active');
  await expect(frame.locator('.lyric-head')).toContainText('/ 29');
  const firstLine = await current.innerText();
  await expect.poll(async () => frame.locator('.lyric-line.is-active .lyric-word.is-current').evaluate(el => parseFloat((el as HTMLElement).style.getPropertyValue('--fill')))).toBeGreaterThan(5);
  const firstFill = await current.locator('.lyric-word').first().getAttribute('style');
  await expect.poll(() => current.locator('.lyric-word').first().getAttribute('style')).not.toBe(firstFill);
  await expect.poll(() => current.innerText(), { timeout: 12_000 }).not.toBe(firstLine);
  const cover = frame.locator('.media-art-lg img');
  await expect(cover).toHaveAttribute('src', 'covers/take-me-hand.jpg');
  expect(await cover.evaluate(img => (img as HTMLImageElement).naturalWidth)).toBeGreaterThan(0);
  for (let line = 0; line < 29; line++) {
    const seek = frame.locator('.seek');
    const size = await seek.boundingBox();
    await seek.click({ position: { x: size!.width * (line + 0.4) / 29, y: size!.height / 2 } });
    await expect(frame.locator('.lyric-head')).toContainText(`${String(line + 1).padStart(2, '0')} / 29`);
  }
  await expect(current).toContainText('For you and me');
  await expect(frame.locator('audio, video, .mini-play, .play-btn, .transport')).toHaveCount(0);
  expect(mediaRequests).toEqual([]);
});

test('landscape phone material controls are not covered by the preview', async ({ page }) => {
  await page.setViewportSize({ width: 844, height: 390 });
  await page.goto('/');
  // A deferred frame must not fail before the visitor reaches the controls.
  await page.waitForTimeout(9500);
  const group = page.getByRole('group', { name: '岛材质' });
  await group.getByRole('button', { name: /液态玻璃/ }).click();
  const frame = page.frameLocator('iframe[name="fluidbar-lab"]');
  await expect(frame.locator('body')).toHaveAttribute('data-material', 'liquid');
  await expect(frame.locator('#island')).toHaveAttribute('data-expanded', 'true');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('material choices support keyboard selection and leave the hero independent', async ({ page }) => {
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto('/');
    const product = page.frameLocator('iframe[name="fluidbar-lab"]');
    const hero = page.frameLocator('iframe[name="fluidbar-hero"]');
    const choices = page.getByRole('group', { name: '岛材质' });
    for (const [name, value] of [['黑曜石', 'obsidian'], ['亚克力', 'acrylic'], ['液态玻璃', 'liquid']]) {
      const button = choices.getByRole('button', { name: new RegExp(name) });
      await button.scrollIntoViewIfNeeded();
      await button.focus();
      await page.keyboard.press('Enter');
      await expect(button).toHaveAttribute('aria-pressed', 'true');
      await expect(product.locator('body')).toHaveAttribute('data-material', value);
      await expect(product.locator('#island')).toHaveAttribute('data-expanded', 'true');
      await expect(hero.locator('body')).toHaveAttribute('data-material', 'obsidian');
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
});
