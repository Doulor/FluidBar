import { expect, test } from '@playwright/test';

for (const width of [1440, 390]) {
  test(`download statistics remain readable at ${width}px without extra controls`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.route('**/t/v1/public-stats', route => route.fulfill({ json: {
      schema: 1, downloads: { count: 12345, updatedAt: '2026-10-30T00:00:00Z' },
      activeInstalls: { count: 0, windowHours: 24, updatedAt: '2026-10-30T00:00:00Z' },
    } }));
    await page.goto('/');
    const stats = page.getByLabel('下载与使用统计');
    await stats.scrollIntoViewIfNeeded();
    await expect(stats.locator('[data-stat="downloads"]')).toHaveText('12,345');
    await expect(stats.locator('[data-stat="active"]')).toHaveText('0');
    await expect(stats).toContainText('不代表实时在线');
    await expect(stats.getByRole('button')).toHaveCount(0);
    await expect(page.locator('.lab-scenes, .market-browser, .plugin-purpose')).toHaveCount(0);
    await expect(page.locator('.quality-layout > .material-artwork')).toHaveCount(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await expect(page.locator('.download-primary')).toHaveAttribute('href', /FluidBar-4\.0\.0-win-x64\.zip$/);
  });
}

test('unavailable statistics do not become fake zero counts', async ({ page }) => {
  await page.route('**/t/v1/public-stats', route => route.fulfill({ json: {
    schema: 1, downloads: { count: null, updatedAt: null },
    activeInstalls: { count: null, windowHours: 24, updatedAt: '2026-10-30T00:00:00Z' },
  } }));
  await page.goto('/');
  await expect(page.getByLabel('下载与使用统计')).toHaveAttribute('aria-busy', 'false');
  await expect(page.locator('[data-stat="downloads"]')).toHaveText('—');
  await expect(page.locator('[data-stat="active"]')).toHaveText('—');
});

test('production statistics come from the real aggregate service', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByLabel('下载与使用统计')).toHaveAttribute('aria-busy', 'false', { timeout: 20_000 });
  await expect(page.locator('[data-stat="downloads"]')).toHaveText(/^[\d,]+$/);
  await expect(page.locator('[data-stat="active"]')).toHaveText(/^[\d,]+$/);
});
