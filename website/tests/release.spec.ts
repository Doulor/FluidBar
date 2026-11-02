import { expect, test } from '@playwright/test';
import { RELEASE } from '../src/data/release';

test('release metadata and every Windows download point to the verified stable asset', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveTitle('FluidBar · Windows 上的灵动岛');
  const downloads = page.getByRole('link', { name: '下载 Windows 版', exact: true });
  await expect(downloads).toHaveCount(2);
  for (const link of await downloads.all()) await expect(link).toHaveAttribute('href', RELEASE.download);
  const metadata = JSON.parse(await page.locator('script[type="application/ld+json"]').innerText());
  expect(metadata.softwareVersion).toBe(RELEASE.version);
  expect(metadata.downloadUrl).toBe(RELEASE.download);
  await expect(page.locator('.release-link')).toContainText(RELEASE.version);
  await expect(page.locator('.quality-source')).toContainText(RELEASE.version);
  await expect(page.locator('.brand')).toHaveText(['FluidBar', 'FluidBar']);
  await expect(page.locator('link[rel="canonical"]')).toHaveAttribute('href', 'https://fluidbar.doulor.cn/');
  await expect(page.locator('meta[property="og:image"]')).toHaveAttribute('content', 'https://fluidbar.doulor.cn/og-image.png');
  await page.getByText('如何核对下载文件？', { exact: true }).click();
  await expect(page.locator('.download-hash')).toHaveText(RELEASE.sha256);
  await expect(page.locator('.download-hash')).toBeVisible();
});

test('featured optional plugins exist in the signed catalog and their listing pages', async ({ page }) => {
  await page.goto('/');
  // Use the visitor's browser network path, including the system proxy.
  const catalog = await page.evaluate(async () => {
    const response = await fetch('/market/index.json', { signal: AbortSignal.timeout(15000) });
    if (!response.ok) throw new Error(`Catalog HTTP ${response.status}`);
    return response.json();
  });
  const links = page.locator('.plugin-catalog a');
  await expect(links).toHaveCount(2);
  for (const link of await links.all()) {
    const href = await link.getAttribute('href');
    const id = href!.split('/').filter(Boolean).at(-2);
    expect(catalog.plugins.some((plugin: { id: string }) => plugin.id === id)).toBe(true);
    const listing = await page.evaluate(async url => {
      const response = await fetch(url, { signal: AbortSignal.timeout(15000) });
      return { status: response.status, body: await response.text() };
    }, href!);
    expect(listing.status).toBe(200);
    expect(listing.body).toContain('默认安装');
  }
});

test('wide desktop preview preserves product geometry and fits small screens', async ({ page }) => {
  for (const width of [1440, 1024, 768, 390, 320]) {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto('/');
    const frame = page.frameLocator('iframe[name="fluidbar-hero"]');
    await expect(frame.locator('body.ready')).toBeAttached();
    await expect.poll(() => frame.locator('#island').evaluate(el => [el.clientWidth, el.clientHeight])).toEqual([472, 284]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    const cta = await page.locator('.hero-actions .button').boundingBox();
    expect(cta!.y + cta!.height).toBeLessThan(700);
    const preview = await page.locator('.hero-stage .prototype-frame').boundingBox();
    expect(preview!.x).toBeGreaterThanOrEqual(0);
    expect(preview!.x + preview!.width).toBeLessThanOrEqual(width);
  }
});
