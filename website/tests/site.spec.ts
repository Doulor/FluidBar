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
    await expect(page.getByRole('heading', { level: 1 })).toContainText('桌面之上');
    await expect(page.getByRole('link', { name: '下载预览版' }).last()).toBeVisible();

    const frame = await waitForFrame(page, HERO_FRAME);
    const island = frame.locator('#island');
    await expect(island).toHaveAttribute('data-expanded', 'true');
    await expect(page.locator('.hero-page-rail .is-active')).toContainText('主页');

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

  test('lab drives real compact states, expansion and pages', async ({ page }) => {
    await page.goto('/');
    await page.locator('#story').scrollIntoViewIfNeeded();
    const frame = await waitForFrame(page, LAB_FRAME);
    const island = frame.locator('#island');

    await page.locator('.lab-step').nth(1).locator('.lab-step__head').click();
    await expect(island).toHaveAttribute('data-view', 'media');
    await expect(page.getByRole('button', { name: /媒体.*226/ })).toBeVisible();
    await expect(page.locator('.music-study')).toContainText('Take Me Hand');
    await expect(page.locator('.music-study')).toContainText('Stay close to me');
    await expect(page.locator('.music-study__lyric-line')).toHaveCount(5);
    await expect(page.locator('.music-study__lyric-word.is-current')).toHaveText('my');
    await expect(frame.locator('.media-lyric .lyric-word')).toHaveCount(3);
    await expect(frame.locator('.mini-play, .play-btn, .transport')).toHaveCount(0);
    await page.waitForTimeout(500);
    const mediaWidth = await island.evaluate((element) => element.getBoundingClientRect().width);
    expect(mediaWidth).toBeGreaterThan(220);

    await page.getByRole('button', { name: /通知 248/ }).click();
    await expect(island).toHaveAttribute('data-view', 'notifications');

    await page.locator('.lab-step').nth(2).locator('.lab-step__head').click();
    await expect(island).toHaveAttribute('data-expanded', 'true');

    await page.locator('.step-options--pages button').nth(2).click();
    await expect(frame.locator('.pager-title')).toContainText('通知');

    await page.locator('.lab-step').nth(4).locator('.lab-step__head').click();
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

    await page.getByRole('button', { name: '液态玻璃', exact: true }).click();
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
