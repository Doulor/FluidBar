// Rebuild the Windows icon and in-app logo from the canonical SVG; no asset is redesigned here.
import { createRequire } from 'node:module';
import { readFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

const root = fileURLToPath(new URL('../', import.meta.url));
const require = createRequire(path.join(root, 'website/package.json'));
const { chromium } = require('@playwright/test');
const assets = path.join(root, 'src/FluidBar.App/Assets');
const reports = path.join(root, 'build/reports/app-icon');
await mkdir(reports, { recursive: true });
const svg = await readFile(path.join(assets, 'fluidbar.svg'), 'utf8');
const browser = await chromium.launch({ channel: 'msedge', headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 1024, height: 1024 }, deviceScaleFactor: 1 });
  await page.setContent(`<style>html,body{margin:0;background:transparent}svg{display:block;width:1024px;height:1024px}</style>${svg}`);
  await page.locator('svg').screenshot({ path: path.join(reports, 'master.png'), omitBackground: true });
} finally {
  await browser.close();
}
execFileSync('python', [path.join(root, 'scripts/generate-app-icons.py'), path.join(reports, 'master.png'), assets, reports], { stdio: 'inherit' });
