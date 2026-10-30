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

test('mobile material selector keeps the expanded translucent preview in view', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');
  const group = page.getByRole('group', { name: '岛材质' });
  const frame = page.frameLocator('iframe[name="fluidbar-lab"]');
  await group.scrollIntoViewIfNeeded();
  await expect(frame.locator('body.ready')).toBeAttached();
  for (const [name, value, maxAlpha] of [['黑曜石', 'obsidian', 1], ['亚克力', 'acrylic', 0.7], ['液态玻璃', 'liquid', 0.4]] as const) {
    await group.getByRole('button', { name, exact: true }).click();
    await expect(frame.locator('body')).toHaveAttribute('data-material', value);
    await expect(frame.locator('#island')).toHaveAttribute('data-expanded', 'true');
    await expect.poll(() => frame.locator('#island').evaluate(el => [el.clientWidth, el.clientHeight])).toEqual([440, 284]);
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
  await group.getByRole('button', { name: '液态玻璃', exact: true }).click();
  const frame = page.frameLocator('iframe[name="fluidbar-lab"]');
  await expect(frame.locator('body')).toHaveAttribute('data-material', 'liquid');
  await expect(frame.locator('#island')).toHaveAttribute('data-expanded', 'true');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('material study supports keyboard selection without changing the real island', async ({ page }) => {
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto('/');
    await page.getByRole('group', { name: '岛材质' }).getByRole('button', { name: '黑曜石', exact: true }).click();
    const product = page.frameLocator('iframe[name="fluidbar-lab"]');
    await expect(product.locator('body')).toHaveAttribute('data-material', 'obsidian');

    const study = page.getByRole('group', { name: '材质研究样本' });
    for (const [name, description] of [
      ['黑曜石', '细腻的边缘反光'],
      ['亚克力', '一层薄雾'],
      ['液态玻璃', '通透的表面和亮边'],
    ]) {
      const button = study.getByRole('button', { name: new RegExp(`${name}.*材质研究`) });
      await button.scrollIntoViewIfNeeded();
      await button.focus();
      await page.keyboard.press('Enter');
      await expect(button).toHaveAttribute('aria-pressed', 'true');
      await expect(page.locator('.optics-caption')).toContainText(description);
      await expect(product.locator('body')).toHaveAttribute('data-material', 'obsidian');
    }
    const bounds = await page.locator('.optics-stage').boundingBox();
    expect(bounds!.x).toBeGreaterThanOrEqual(0);
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
});
