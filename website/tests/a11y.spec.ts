import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

test('site chrome has no serious accessibility violations', async ({ page }) => {
  await page.goto('/');
  await page.waitForTimeout(1600);

  const results = await new AxeBuilder({ page }).analyze();
  const serious = results.violations
    .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
    .map((violation) => ({
      id: violation.id,
      impact: violation.impact,
      nodes: violation.nodes
        // The iframe is the real product prototype. Its own demo typography is
        // preserved rather than restyled for the marketing page; audit the site
        // chrome separately so a product color token is not silently changed.
        .filter((node) => !String(node.target[0] ?? '').startsWith('iframe'))
        .map((node) => node.html),
    }))
    .filter((violation) => violation.nodes.length > 0);

  expect(serious).toEqual([]);
});

test('reduced motion keeps the real island usable', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/');
  const frame = page.frameLocator('iframe[title^="FluidBar 真实交互原型"]');
  await expect(frame.locator('body.ready')).toBeAttached({ timeout: 20_000 });
  const island = frame.locator('#island');
  await island.click();
  await expect(island).toHaveAttribute('data-expanded', 'true');
  await page.keyboard.press('Escape');
  await expect(island).toHaveAttribute('data-expanded', 'false');
});
