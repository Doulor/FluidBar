// Capture the real same-origin embed, never a second implementation of the UI.
// Start pnpm dev first, then: node scripts/capture-posters.mjs
import { chromium } from '@playwright/test';
import { fileURLToPath } from 'node:url';
const origin = process.env.POSTER_BASE_URL || 'http://127.0.0.1:4173';
const browser = await chromium.launch({ channel: 'msedge' });
try {
  const context = await browser.newContext({ viewport: { width: 520, height: 444 }, deviceScaleFactor: 2, reducedMotion: 'reduce' });
  const page = await context.newPage();
  for (const [state, file] of [['idle', 'hero-idle-daylight.png'], ['expanded', 'lab-expanded-daylight.png']]) {
    await page.goto(`${origin}/island/embed.html?state=${state}&tab=home`);
    await page.locator('body.ready').waitFor();
    await page.evaluate(() => document.fonts.ready);
    await page.locator('#island').evaluate(async element => {
      await Promise.all(element.getAnimations().map(animation => animation.finished.catch(() => {})));
    });
    await page.screenshot({ path: fileURLToPath(new URL(`../public/posters/${file}`, import.meta.url)) });
    console.log(`Captured ${file} from the real ${state} embed`);
  }
} finally { await browser.close(); }
