import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtemp, mkdir, readFile, writeFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { syncMarketplace } from '../sync-marketplace.mjs';
import { renderCatalog, renderListing } from '../../marketplace/render.mjs';

const plugin = id => ({ id, name: { 'zh-Hans': id }, summary: { 'en-US': 'Optional plugin' }, publisher: { displayName: 'FluidBar' }, latest: { version: '2.0.0', artifact: { url: `https://example.com/${id}.fbp`, sha256: 'a'.repeat(64) } } });
async function fixture(action) {
  const temporary = await mkdtemp(path.join(os.tmpdir(), 'fluidbar-market-site-'));
  try {
    const sourceDirectory = path.join(temporary, 'source');
    const targetDirectory = path.join(temporary, 'target');
    const assetsDirectory = path.join(temporary, 'assets');
    await mkdir(path.join(sourceDirectory, 'plugins'), { recursive: true });
    await mkdir(path.join(assetsDirectory, 'sample-minimal'), { recursive: true });
    await writeFile(path.join(assetsDirectory, 'sample-minimal/icon.png'), 'icon-fixture');
    const index = { schemaVersion: 1, generatedAt: '2026-10-31T00:00:00Z', signatures: [{ value: 'preserved-signature' }], plugins: [plugin('fluidbar-timer'), plugin('previously-unknown-plugin')] };
    const bytes = Buffer.from(JSON.stringify(index, null, 3) + '\n');
    await writeFile(path.join(sourceDirectory, 'index.json'), bytes);
    await writeFile(path.join(sourceDirectory, 'keys.json'), '[]');
    for (const entry of index.plugins)
      await writeFile(path.join(sourceDirectory, 'plugins', `${entry.id}.json`), JSON.stringify({ schemaVersion: 1, id: entry.id, versions: [{ version: '2.0.0' }, { version: '1.0.0' }] }));
    await action({ sourceDirectory, targetDirectory, assetsDirectory }, bytes);
  } finally { await rm(temporary, { recursive: true, force: true }); }
}

test('New plugins get listings and resources while signed bytes and full history stay unchanged', async () => fixture(async (options, bytes) => {
  const result = await syncMarketplace(options);
  assert.deepEqual(result.plugins, ['fluidbar-timer', 'previously-unknown-plugin']);
  assert.deepEqual(await readFile(path.join(options.targetDirectory, 'index.json')), bytes);
  for (const id of result.plugins) {
    assert.match(await readFile(path.join(options.targetDirectory, `listings/${id}/index.html`), 'utf8'), new RegExp(id));
    assert.equal((await readFile(path.join(options.targetDirectory, `assets/${id}/icon.png`))).toString(), 'icon-fixture');
    assert.equal(JSON.parse(await readFile(path.join(options.targetDirectory, `plugins/${id}.json`))).versions.length, 2);
  }
}));

test('Normal website builds preserve the fetched live catalog instead of a checked-in snapshot', async () => fixture(async (options, bytes) => {
  const sourceDirectory = options.sourceDirectory;
  const result = await syncMarketplace({ ...options, sourceDirectory: undefined, fetchImpl: async url => {
    const relative = url.pathname.replace(/^\/market\//, '');
    return new Response(await readFile(path.join(sourceDirectory, relative)));
  } });
  assert.equal(result.plugins.length, 2);
  assert.deepEqual(await readFile(path.join(options.targetDirectory, 'index.json')), bytes);
}));

test('Unavailable live catalog fails rather than silently deploying an old fixture', async () => fixture(async options => {
  await assert.rejects(syncMarketplace({ ...options, sourceDirectory: undefined, fetchImpl: async () => new Response('', { status: 503 }) }), /refusing a stale/);
}));

test('Only obsolete generated plugin output is removed when the catalog changes', async () => fixture(async options => {
  await mkdir(path.join(options.targetDirectory, 'assets/obsolete-plugin'), { recursive: true });
  await writeFile(path.join(options.targetDirectory, 'index.json'), JSON.stringify({ plugins: [{ id: 'obsolete-plugin' }] }));
  await writeFile(path.join(options.targetDirectory, 'assets/obsolete-plugin/icon.png'), 'old');
  await syncMarketplace(options);
  await assert.rejects(readFile(path.join(options.targetDirectory, 'assets/obsolete-plugin/icon.png')), { code: 'ENOENT' });
  assert.equal((await readFile(path.join(options.targetDirectory, 'assets/fluidbar-timer/icon.png'))).toString(), 'icon-fixture');
}));

test('Static pages escape catalog text and preserve exact downloads without inventing unknown plugin details', () => {
  const entry = plugin('unknown-tool');
  entry.name = '<script>alert("name")</script>';
  entry.summary = 'A "quoted" <img src=x onerror=alert(1)> summary';
  entry.publisher.displayName = '<svg onload=alert(1)>';
  const assets = { css: '/market/ui/market.css', js: '/market/ui/market.js' };
  for (const html of [renderCatalog({ plugins: [entry] }, assets), renderListing(entry, { versions: [entry.latest] }, assets)]) {
    assert.ok(!html.includes('<script>alert'));
    assert.ok(!html.includes('<img src=x'));
    assert.ok(!html.includes('<svg onload'));
    assert.ok(html.includes('&lt;script&gt;'));
    assert.ok(!html.includes('undefined'));
    assert.ok(!html.includes('1–180'));
  }
  const html = renderListing(entry, { versions: [entry.latest] }, assets);
  assert.ok(html.includes('https://example.com/unknown-tool.fbp'));
  assert.ok(html.includes('a'.repeat(64)));
});

test('Withdrawn or blocked catalog entries have no install download action', () => {
  const assets = { css: '/market/ui/market.css', js: '/market/ui/market.js' };
  for (const state of ['yanked', 'blocked', 'inactive']) {
    const entry = plugin('unavailable-tool');
    if (state === 'yanked') entry.latest.yanked = true;
    if (state === 'blocked') entry.blocked = { reason: 'test' };
    if (state === 'inactive') entry.status = 'removed';
    const html = renderListing(entry, { versions: [entry.latest] }, assets);
    assert.ok(!html.includes('class="download-button"'));
    assert.ok(html.includes('此版本暂不可下载'));
  }
});
