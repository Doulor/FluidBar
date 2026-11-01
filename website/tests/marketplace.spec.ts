import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

for (const width of [1440, 390]) {
  test(`market catalog search, filters and real detail pages work at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    await page.goto('/market/');
    await expect(page).toHaveTitle('插件市场 · FluidBar');
    await expect(page.getByRole('heading', { level: 1 })).toContainText('添一项本领');
    const cards = page.locator('[data-plugin-card]:visible');
    await expect(cards).toHaveCount(4);
    await page.getByRole('button', { name: /开发示例/ }).click();
    await expect(cards).toHaveCount(2);
    await expect(cards.first()).toContainText('最小示例');
    await page.getByRole('button', { name: /实用工具/ }).click();
    await page.getByRole('searchbox', { name: '搜索插件' }).fill('倒计时');
    await expect(cards).toHaveCount(1);
    await page.reload();
    await expect(cards).toHaveCount(1);
    await expect(page.getByRole('searchbox', { name: '搜索插件' })).toHaveValue('倒计时');
    await cards.locator('a').click();
    await expect(page).toHaveURL(/\/market\/listings\/fluidbar-timer\/$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('倒计时');
    await expect(page.getByRole('link', { name: /下载安装包/ })).toHaveAttribute('href', /\/fluidbar-timer-1\.0\.0\.fbp$/);
    await page.getByText('文件校验与手动安装', { exact: true }).click();
    await expect(page.locator('.package-hash')).toHaveText('b480ee3122768976c0a6fbe2aeff9cbf6f12ac20c8c8384864cee1d2ff26ad41');
    await page.getByRole('link', { name: '继续发现插件' }).click();
    await page.getByRole('searchbox', { name: '搜索插件' }).fill('不存在的插件');
    await expect(cards).toHaveCount(0);
    await expect(page.getByRole('heading', { name: '没有找到匹配的插件' })).toBeVisible();
    await page.getByRole('button', { name: /清除筛选/ }).click();
    await expect(cards).toHaveCount(4);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect(errors).toEqual([]);
  });
}

test('market pages expose real metadata, accessible controls and no horizontal overflow', async ({ page }) => {
  for (const path of ['/market/', '/market/listings/fluidbar-file-shelf/', '/market/listings/fluidbar-timer/', '/market/listings/sample-minimal/', '/market/listings/sample-rich/']) {
    await page.goto(path);
    await expect(page.locator('link[rel="canonical"]')).toHaveAttribute('href', `https://fluidbar.doulor.cn${path}`);
    await expect(page.locator('meta[name="description"]')).not.toHaveAttribute('content', '');
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations.filter(v => v.impact === 'serious' || v.impact === 'critical')).toEqual([]);
    for (const width of [1440, 768, 390, 320]) {
      await page.setViewportSize({ width, height: 1000 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${path} at ${width}px`).toBe(true);
    }
  }
});

test('static catalog and download stay usable without JavaScript; unknown routes are 404', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, proxy: test.info().project.use.proxy });
  const page = await context.newPage();
  const origin = test.info().project.use.baseURL!;
  await page.goto(`${origin}/market/`);
  await expect(page.locator('[data-plugin-card]')).toHaveCount(4);
  await expect(page.locator('[data-catalog-controls]')).toBeHidden();
  await page.locator('[data-plugin-card]').first().getByRole('link').click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('文件暂存');
  await expect(page.getByRole('link', { name: /下载安装包/ })).toBeVisible();
  await expect(page.locator('[data-copy-hash]')).toBeHidden();
  for (const path of ['/market/not-a-plugin.json', '/market/listings/not-a-plugin/', '/schemas/not-a-schema.json']) {
    const response = await page.goto(`${origin}${path}`);
    expect(response!.status()).toBe(404);
    await expect(page.locator('#hero-title')).toHaveCount(0);
  }
  await context.close();
});

test('checksum copy confirms only successful clipboard writes and supports failure', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await page.goto('/market/listings/fluidbar-timer/');
  await page.getByText('文件校验与手动安装', { exact: true }).click();
  await page.getByRole('button', { name: '复制 SHA-256 校验值' }).click();
  await expect(page.locator('.copy-status')).toContainText('已复制');
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(await page.locator('.package-hash').innerText());
  await page.evaluate(() => { navigator.clipboard.writeText = async () => { throw new Error('Permission denied'); }; });
  await page.getByRole('button', { name: '复制 SHA-256 校验值' }).click();
  await expect(page.locator('.copy-status')).toContainText('复制未成功');
});
