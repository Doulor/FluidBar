import { readFile, writeFile, mkdir, rm } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const localized = value => typeof value === 'string' ? value : value?.['zh-Hans'] ?? value?.['en-US'] ?? '';
const safeId = id => /^[a-z0-9][a-z0-9-]{1,62}$/.test(id);
const css = 'body{margin:0;background:#f3f5f6;color:#10151b;font:16px/1.7 "Segoe UI",sans-serif}main{max-width:900px;margin:auto;padding:64px 24px}a{color:#2447ff}h1{font-size:42px;line-height:1.2}article{background:white;border:1px solid #d8dee5;border-radius:14px;padding:24px;margin:16px 0}img{width:64px;height:64px;float:left;margin:0 20px 12px 0}p{color:#5d6874}code{overflow-wrap:anywhere;font-size:12px}.meta{clear:both}';
const page = (title, body) => `<!doctype html><html lang="zh-Hans"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>${escape(title)} · FluidBar</title><style>${css}</style></head><body><main>${body}</main></body></html>\n`;

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
  const cards = [];
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
    const name = escape(localized(plugin.name));
    const summary = escape(localized(plugin.summary));
    const latest = plugin.latest;
    const download = new URL(latest.artifact.url);
    if (download.protocol !== 'https:') throw new Error(`Package URL must use HTTPS: ${plugin.id}`);
    const card = `<article><img src="/market/assets/${plugin.id}/icon.png" alt=""><h2><a href="/market/listings/${plugin.id}/">${name}</a></h2><p>${summary}</p><p class="meta">v${escape(latest.version)} · ${escape(plugin.publisher.displayName)}</p></article>`;
    cards.push(card);
    files.push([`listings/${plugin.id}/index.html`, page(localized(plugin.name), `<a href="/market/">插件市场</a><article><img src="/market/assets/${plugin.id}/icon.png" alt=""><h1>${name}</h1><p>${summary}</p><p class="meta">v${escape(latest.version)} · API ${escape(latest.apiVersion)} · ${escape(plugin.publisher.displayName)}</p><p>在 FluidBar 的「设置 → 插件市场」中选择安装，插件不会默认安装。</p><p><a href="${escape(download.href)}">下载签名安装包</a></p><p>SHA-256：<code>${escape(latest.artifact.sha256)}</code></p></article>`)]);
  }
  files.push(['index.html', page('插件市场', `<p>FLUIDBAR / MARKETPLACE</p><h1>可选插件</h1><p>安装由你决定。以下条目来自当前签名市场目录。</p>${cards.join('')}`)]);
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
