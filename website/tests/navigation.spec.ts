import { expect, test, type Page } from '@playwright/test';

const chapters = [
  ['体验', 'experience'], ['外观', 'appearance'], ['插件', 'plugins'],
  ['品质', 'performance'], ['下载', 'download'],
] as const;

async function expectLanding(page: Page, id: string) {
  await expect.poll(() => page.locator(`#${id}`).evaluate(el => Math.round(el.getBoundingClientRect().top))).toBeGreaterThanOrEqual(70);
  await expect.poll(() => page.locator(`#${id}`).evaluate(el => Math.round(el.getBoundingClientRect().top))).toBeLessThanOrEqual(112);
}

test('desktop chapters have matching navigation, landing positions and scroll highlights', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/');
  await page.evaluate(() => document.fonts.ready);
  expect(await page.locator('main > section').evaluateAll(els => els.map(el => el.id))).toEqual(chapters.map(([, id]) => id));
  const nav = page.getByRole('navigation', { name: '主导航' });
  for (const [label, id] of chapters) {
    const link = nav.getByRole('link', { name: label, exact: true });
    await link.click();
    await expect(page).toHaveURL(new RegExp(`#${id}$`));
    await expectLanding(page, id);
    await expect(link).toHaveAttribute('aria-current', 'page');
  }
  await page.locator('#appearance').evaluate(el => window.scrollTo(0, el.getBoundingClientRect().top + scrollY - 96));
  await expect(nav.getByRole('link', { name: '外观', exact: true })).toHaveAttribute('aria-current', 'page');
  await expect(page.locator('#appearance #material-study')).toHaveCount(1);
  await expect(page.locator('.quality-layout > .material-artwork')).toHaveCount(1);
});

test('all five chapters can be reached from the mobile menu', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/');
  await page.evaluate(() => document.fonts.ready);
  for (const [label, id] of chapters) {
    await page.getByRole('button', { name: '打开导航' }).click();
    await page.getByRole('navigation', { name: '移动导航' }).getByRole('link', { name: label, exact: true }).click();
    await expect(page.getByRole('navigation', { name: '移动导航' })).toHaveCount(0);
    await expectLanding(page, id);
  }
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('chapter and subsection deep links survive reload and browser history', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  for (const [id, chapter] of [['appearance', 'appearance'], ['performance', 'performance'], ['material-study', 'appearance'], ['story', 'experience']]) {
    await page.goto(`/#${id}`);
    await page.evaluate(() => document.fonts.ready);
    await expectLanding(page, id);
    await expect(page.locator(`.nav-desktop a[href="#${chapter}"]`)).toHaveAttribute('aria-current', 'page');
  }
  await page.getByRole('navigation', { name: '主导航' }).getByRole('link', { name: '外观', exact: true }).click();
  await page.getByRole('navigation', { name: '主导航' }).getByRole('link', { name: '品质', exact: true }).click();
  await page.goBack();
  await expectLanding(page, 'appearance');
  await expect(page.locator('.nav-desktop a[href="#appearance"]')).toHaveAttribute('aria-current', 'page');
  await page.goForward();
  await expectLanding(page, 'performance');
});
