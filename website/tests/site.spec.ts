import { expect, test, type Page } from '@playwright/test';

const HERO_FRAME = 'iframe[title^="FluidBar 真实交互原型"]';
const LAB_FRAME = 'iframe[title="FluidBar 真实灵动岛原型"]';

async function waitForFrame(page: Page, selector: string) {
  const frame = page.frameLocator(selector);
  await expect(frame.locator('body.ready')).toBeAttached({ timeout: 20_000 });
  return frame;
}

test.describe('FluidBar official site', () => {
  test('hero renders and the real island expands/collapses', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (error) => {
      errors.push(error.message);
    });

    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1 })).toContainText('让桌面，轻一点');
    await expect(page.getByRole('link', { name: '下载 Windows 版' }).last()).toBeVisible();

    const frame = await waitForFrame(page, HERO_FRAME);
    const island = frame.locator('#island');
    await expect(island).toHaveAttribute('data-expanded', 'true');
    await expect(page.locator('.hero-page-rail .is-active')).toContainText('媒体');

    await page.waitForTimeout(600);
    const expandedWidth = await island.evaluate((element) => element.getBoundingClientRect().width);
    expect(expandedWidth).toBeGreaterThan(430);

    await page.locator('.hero-page-rail').getByRole('button', { name: /媒体/ }).click();
    await expect(frame.locator('.pager-title')).toContainText('媒体');

    await frame.locator('#pagerCollapse').click();
    await expect(island).toHaveAttribute('data-expanded', 'false');

    await island.click();
    await expect(island).toHaveAttribute('data-expanded', 'true');
    await page.keyboard.press('Escape');
    await expect(island).toHaveAttribute('data-expanded', 'false');

    expect(errors).toEqual([]);
  });

  test('experience selector drives real compact states, expansion and pages', async ({ page }) => {
    await page.goto('/');
    const frame = await waitForFrame(page, HERO_FRAME);
    const island = frame.locator('#island');
    const selector = page.getByRole('combobox', { name: '预览状态' });
    await selector.selectOption('media');
    await expect(island).toHaveAttribute('data-view', 'media');
    await expect(frame.locator('.media-lyric .lyric-word').first()).toBeVisible();
    await expect(frame.locator('.mini-play, .play-btn, .transport')).toHaveCount(0);
    await expect.poll(() => island.evaluate(element => element.clientWidth)).toBe(226);
    await selector.selectOption('notifications');
    await expect(island).toHaveAttribute('data-view', 'notifications');
    await selector.selectOption('expanded');
    await expect(island).toHaveAttribute('data-expanded', 'true');
    await page.getByRole('navigation', { name: '原型页面' }).getByRole('button', { name: /通知/ }).click();
    await expect(frame.locator('.pager-title')).toContainText('通知');
    await selector.selectOption('idle');
    await expect(island).toHaveAttribute('data-expanded', 'false');
    await expect(island).toHaveAttribute('data-view', 'idle');
  });

  test('appearance controls write into the real prototype', async ({ page }) => {
    await page.goto('/');
    await page.locator('#appearance').scrollIntoViewIfNeeded();
    const frame = await waitForFrame(page, LAB_FRAME);
    const body = frame.locator('body');

    await page.getByRole('button', { name: 'Dock', exact: true }).click();
    await expect(body).toHaveAttribute('data-mode', 'dock');
    await expect(frame.locator('#dock')).toHaveCSS('display', 'flex');

    await page.getByRole('button', { name: /液态玻璃/ }).click();
    await expect(body).toHaveAttribute('data-material', 'liquid');
    await expect(page.getByText('强调色')).toHaveCount(0);
  });

  test('mobile layout has no horizontal overflow and menu works', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/');
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(overflow).toBe(false);

    await page.getByRole('button', { name: '打开导航' }).click();
    await expect(page.getByRole('navigation', { name: '移动导航' })).toBeVisible();
    await page.getByRole('navigation', { name: '移动导航' }).getByRole('link', { name: '插件' }).click();
    await expect(page.locator('#plugins')).toBeVisible();
  });

  test('no page errors on the landing page', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (error) => {
      errors.push(error.message);
    });
    await page.goto('/');
    await page.waitForTimeout(1500);
    expect(errors).toEqual([]);
  });
});
