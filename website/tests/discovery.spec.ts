import { expect, test } from '@playwright/test';

test('slow preview stays mounted and recovers after the loading notice', async ({ page }) => {
  await page.route('**/island/embed.html?state=expanded*', async route => {
    await new Promise(resolve => setTimeout(resolve, 10_500));
    await route.continue();
  });
  // Observe the loading notice before window.load, which can itself wait on
  // the deliberately delayed iframe on a fast production script load.
  await page.goto('/', { waitUntil: 'domcontentloaded' });
  await expect(page.getByText('原型仍在加载，可继续等待或重试')).toBeVisible({ timeout: 15_000 });
  const hero = page.frameLocator('iframe[name="fluidbar-hero"]');
  await expect(hero.locator('body.ready')).toBeAttached({ timeout: 20_000 });
  await expect(page.getByRole('button', { name: '重新加载预览' })).toHaveCount(0);
  await hero.locator('#pagerCollapse').click();
  await expect(hero.locator('#island')).toHaveAttribute('data-expanded', 'false');
});

for (const width of [1440, 390]) {
  test(`scenes, plugin discovery and download work at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    await page.goto('/');
    const scenes = page.getByRole('group', { name: '使用场景' });
    const frame = page.frameLocator('iframe[name="fluidbar-lab"]');
    for (const [label, title] of [['听歌看歌词', '媒体'], ['展开看通知', '通知'], ['查看剪贴板', '剪贴板']]) {
      await scenes.getByRole('button', { name: new RegExp(label) }).click();
      await expect(frame.locator('.pager-title')).toContainText(title);
      await expect(frame.locator('#island')).toHaveAttribute('data-expanded', 'true');
    }
    await expect(page.locator('.code-panel pre')).toHaveCount(0);
    await page.getByRole('button', { name: '开发者信息', exact: true }).click();
    await expect(page.locator('.code-panel pre')).toContainText('capabilities');
    await page.getByRole('button', { name: '看完整 manifest' }).click();
    await expect(page.locator('.code-panel pre')).toContainText('schemaVersion');
    await page.getByRole('button', { name: '查看用途' }).click();
    await page.locator('.plugin-list').getByRole('button', { name: /^Agent/ }).click();
    await page.getByRole('button', { name: /在岛上体验 Agent/ }).click();
    await expect(frame.locator('.pager-title')).toContainText('Agent');
    // Scroll observers must not immediately override a scene selected in another section.
    await page.waitForTimeout(1800);
    await expect(frame.locator('.pager-title')).toContainText('Agent');

    const market = page.locator('#marketplace');
    await expect(market.locator('article')).toHaveCount(2);
    await market.getByRole('searchbox', { name: '搜索插件' }).fill('sample-minimal');
    await expect(market.locator('article')).toHaveCount(1);
    const listing = market.getByRole('link', { name: /查看详情/ });
    const href = await listing.getAttribute('href');
    const response = await page.request.get(href!);
    expect(response.ok()).toBe(true);
    expect(await response.text()).toContain('sample-minimal');
    await market.getByRole('searchbox').fill('no-matching-plugin');
    await expect(market.getByRole('status')).toContainText('没有匹配结果');
    await market.getByRole('searchbox').fill('');
    await market.getByRole('combobox', { name: '分类' }).selectOption('media');
    await expect(market.locator('article')).toHaveCount(1);
    await expect(market.locator('article')).toContainText('丰富示例');

    const download = page.locator('.download-primary');
    await expect(download).toHaveAttribute('href', /v4\.0\.0-preview\.2\/FluidBar-4\.0\.0-preview\.2-win-x64\.zip$/);
    await page.getByText('支持哪些系统？需要安装 .NET 吗？', { exact: true }).click();
    await expect(page.locator('.faq-list details').first()).toHaveAttribute('open', '');
    await expect(page.locator('.faq-list details').first()).toContainText('无需单独安装');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await expect(page.locator('.architecture > .material-artwork')).toHaveCount(1);
    expect(errors).toEqual([]);
  });
}

test('market failure can be retried without replacing the signed data', async ({ page }) => {
  let failed = true;
  await page.route('**/market/index.json', route => failed
    ? route.fulfill({ status: 503, body: 'Unavailable' })
    : route.continue());
  await page.goto('/');
  const market = page.locator('#marketplace');
  await expect(market.getByRole('status')).toContainText('暂时无法读取');
  failed = false;
  await market.getByRole('button', { name: '重新加载市场' }).click();
  await expect(market.locator('article')).toHaveCount(2);
});
