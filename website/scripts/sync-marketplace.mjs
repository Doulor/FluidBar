import { readFile, writeFile, mkdir, rm } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { renderCatalog, renderListing } from '../marketplace/render.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const safeId = id => /^[a-z0-9][a-z0-9-]{1,62}$/.test(id);

/** Preserve signed bytes and version history; generate pages for every catalog entry, including new ids. */
export async function syncMarketplace({
  sourceDirectory,
  sourceUrl = 'https://fluidbar.doulor.cn/market/',
  targetDirectory = path.join(root, 'website/public/market'),
  assetsDirectory = path.join(root, 'website/public/market/assets'),
  fetchImpl = fetch,
} = {}) {
  async function source(relative) {
    if (sourceDirectory) return readFile(path.join(sourceDirectory, relative));
    const response = await fetchImpl(new URL(relative, sourceUrl), { signal: AbortSignal.timeout(20_000), cache: 'no-store' });
    if (!response.ok) throw new Error(`Marketplace source ${relative}: HTTP ${response.status}; refusing a stale checked-in fallback.`);
    return Buffer.from(await response.arrayBuffer());
  }
  const indexBytes = await source('index.json');
  const index = JSON.parse(indexBytes);
  if (index.schemaVersion !== 1 || !Array.isArray(index.plugins) || !index.signatures?.length)
    throw new Error('A signed marketplace index is required.');
  const keys = await source('keys.json');
  const seen = new Set();
  const files = [['index.json', indexBytes], ['keys.json', keys]];
  // Static, independently accessible pages. JS only enhances filtering/copy;
  // signed JSON is copied verbatim, never serialized by the presentation layer.
  const css = await readFile(path.join(root, 'website/marketplace/market.css'));
  const js = await readFile(path.join(root, 'website/marketplace/market.js'));
  const revision = createHash('sha256').update(css).update(js).digest('hex').slice(0, 12);
  const assets = { css: `/market/ui/market.css?v=${revision}`, js: `/market/ui/market.js?v=${revision}` };
  files.push(['ui/market.css', css], ['ui/market.js', js],
    ['ui/market-cn.woff2', await readFile(path.join(root, 'website/src/assets/fonts/NotoSansSC-Market.woff2'))],
    ['ui/satoshi.woff2', await readFile(path.join(root, 'website/src/assets/fonts/Satoshi-Variable.woff2'))]);
  for (const plugin of index.plugins) {
    if (!safeId(plugin.id) || seen.has(plugin.id)) throw new Error('Invalid or duplicate marketplace plugin id.');
    seen.add(plugin.id);
    const historyBytes = await source(`plugins/${plugin.id}.json`);
    const history = JSON.parse(historyBytes);
    if (!history.versions?.some(version => version.version === plugin.latest?.version)
        || (history.id && history.id !== plugin.id)) throw new Error(`Missing matching version history: ${plugin.id}`);
    files.push([`plugins/${plugin.id}.json`, historyBytes]);
    let icon;
    try { icon = await readFile(path.join(assetsDirectory, plugin.id, 'icon.png')); }
    catch (error) {
      if (error.code !== 'ENOENT') throw error;
      icon = await readFile(path.join(assetsDirectory, 'sample-minimal/icon.png'));
    }
    files.push([`assets/${plugin.id}/icon.png`, icon]);
    const latest = plugin.latest;
    const download = new URL(latest.artifact.url);
    if (download.protocol !== 'https:') throw new Error(`Package URL must use HTTPS: ${plugin.id}`);
    files.push([`listings/${plugin.id}/index.html`, renderListing(plugin, history, assets)]);
  }
  files.push(['index.html', renderCatalog(index, assets)]);
  let previous = [];
  try { previous = JSON.parse(await readFile(path.join(targetDirectory, 'index.json'))).plugins ?? []; }
  catch (error) { if (error.code !== 'ENOENT' && !(error instanceof SyntaxError)) throw error; }
  const targetRoot = path.resolve(targetDirectory) + path.sep;
  for (const plugin of previous.filter(entry => safeId(entry.id) && !seen.has(entry.id))) {
    for (const relative of [`plugins/${plugin.id}.json`, `listings/${plugin.id}`, `assets/${plugin.id}`]) {
      const obsolete = path.resolve(targetDirectory, relative);
      if (!obsolete.startsWith(targetRoot)) throw new Error('Obsolete marketplace output escaped its target directory.');
      await rm(obsolete, { recursive: true, force: true });
    }
  }
  // Finish all reads/validation before replacing local output. Never serialize the signed index.
  for (const [relative, bytes] of files) {
    const destination = path.join(targetDirectory, relative);
    await mkdir(path.dirname(destination), { recursive: true });
    await writeFile(destination, bytes);
  }
  return { plugins: [...seen], generatedAt: index.generatedAt, sourceCommit: index.sourceCommit };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const result = await syncMarketplace({ sourceDirectory: process.env.FLUIDBAR_MARKETPLACE_SOURCE_DIR });
  console.log(`marketplace: ${result.plugins.join(', ')}; generated ${result.generatedAt}; source ${result.sourceCommit}`);
}
