import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { ArrowUpRight, ArrowRight, ArrowLeft, DownloadSimple, MagnifyingGlass, Copy, CheckCircle, PuzzlePiece, Code } from '@phosphor-icons/react/ssr';
import { guides, capabilities } from './content.mjs';

export const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
export const localized = value => typeof value === 'string' ? value : value?.['zh-Hans'] ?? value?.['en-US'] ?? '';
const glyph = (icon, size = 20) => renderToStaticMarkup(createElement(icon, { size, weight: 'regular', 'aria-hidden': true, focusable: false }));
const repository = 'https://github.com/Doulor/FluidBar';
const docs = `${repository}/blob/main/docs/plugin/quickstart.md`;
const sample = plugin => plugin.tags?.includes('sample') || plugin.publisher?.id === 'fluidbar-samples';
const kind = plugin => sample(plugin) ? 'samples' : 'tools';
const kindName = plugin => sample(plugin) ? '开发示例' : '实用工具';
const available = plugin => (!plugin.status || plugin.status === 'active') && !plugin.latest.yanked && !plugin.blocked;
const href = plugin => `/market/listings/${plugin.id}/`;
const date = value => value && Number.isFinite(Date.parse(value)) ? new Date(value).toISOString().slice(0, 10).replaceAll('-', '.') : '未提供';
const size = value => Number.isFinite(value) && value > 0 ? `${(value / 1024).toFixed(1)} KB` : '大小未提供';
const secureUrl = value => { try { const url = new URL(value); return url.protocol === 'https:' ? escape(url.href) : null; } catch { return null; } };
const link = (url, text) => `<a href="${url}" target="_blank" rel="noopener noreferrer">${text}${glyph(ArrowUpRight, 16)}</a>`;
const publisher = plugin => `<span>${escape(plugin.publisher?.displayName || '发布者未提供')}</span>${plugin.publisher?.verified ? `<span class="publisher-verified" title="目录标记为已验证发布者">${glyph(CheckCircle, 15)}<span class="sr-only">已验证发布者</span></span>` : ''}`;

function shell({ title, description, pathname, body, assets, detail = false }) {
  const origin = 'https://fluidbar.doulor.cn';
  return `<!doctype html>
<html lang="zh-Hans"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light">
<title>${escape(title)} · FluidBar</title><meta name="description" content="${escape(description)}"><link rel="canonical" href="${origin}${pathname}"><link rel="icon" type="image/svg+xml" href="/favicon.svg">
<meta property="og:title" content="${escape(title)} · FluidBar"><meta property="og:description" content="${escape(description)}"><meta property="og:type" content="website"><meta property="og:url" content="${origin}${pathname}"><meta property="og:image" content="${origin}/og-image.png">
<link rel="stylesheet" href="${assets.css}"><script type="module" src="${assets.js}"></script></head>
<body class="${detail ? 'detail-page' : 'catalog-page'}"><a class="skip-link" href="#main">跳到主要内容</a>
<header class="market-header"><div class="market-shell header-inner"><a class="brand" href="/" aria-label="FluidBar 官网"><img src="/favicon.svg" width="32" height="32" alt="">FluidBar</a><nav aria-label="主导航"><a href="/">官网</a><a href="/market/" ${detail ? '' : 'aria-current="page"'} class="nav-current">插件市场</a><a href="${docs}" target="_blank" rel="noopener noreferrer">开发文档${glyph(ArrowUpRight, 14)}</a></nav><a class="header-download" href="/#download">下载 FluidBar${glyph(DownloadSimple, 16)}</a></div></header>
<main id="main" class="market-shell" tabindex="-1">${body}</main>
<footer class="market-footer market-shell"><a class="brand" href="/"><img src="/favicon.svg" width="24" height="24" alt="">FluidBar</a><p>常驻，但不打扰。</p><nav aria-label="页脚导航">${link(docs, '开发文档')}${link(`${repository}/issues`, '反馈问题')}<a href="/market/index.json">目录数据${glyph(ArrowUpRight, 14)}</a></nav><span>© 2026 Doulor</span></footer>
<p class="copy-status sr-only" role="status" aria-live="polite"></p></body></html>\n`;
}

function card(plugin) {
  const title = localized(plugin.name);
  const search = [title, localized(plugin.summary), plugin.name?.['en-US'], plugin.id, plugin.publisher?.displayName].join(' ');
  return `<article class="plugin-card" data-plugin-card data-kind="${kind(plugin)}" data-search="${escape(search)}">
<a class="plugin-card-link" href="${href(plugin)}"><div class="plugin-card-top"><img class="plugin-icon" src="/market/assets/${plugin.id}/icon.png" width="60" height="60" alt=""><span class="plugin-type">${glyph(sample(plugin) ? Code : PuzzlePiece, 16)}${kindName(plugin)}</span>${glyph(ArrowUpRight, 22)}</div>
<div class="plugin-title-row"><h3>${escape(title)}</h3><span>v${escape(plugin.latest.version)}</span></div><p class="plugin-summary">${escape(localized(plugin.summary))}</p>
<div class="plugin-card-bottom"><span class="publisher">${publisher(plugin)}</span><span>${available(plugin) ? '查看详情' : '暂不可下载'}${glyph(ArrowRight, 15)}</span></div></a></article>`;
}

function installGuide(name) {
  return `<section id="install" class="content-section install-guide"><div class="section-heading"><h2>装进小岛，只需几步。</h2><p>插件不会默认安装，启用哪些由你决定。</p></div><ol class="install-steps"><li><span>01</span><h3>打开 FluidBar</h3><p>进入「设置 → 插件市场」。${name ? `找到「${escape(name)}」。` : '找到想使用的插件。'}</p></li><li><span>02</span><h3>查看并安装</h3><p>阅读插件信息和权限请求，确认后安装。</p></li><li><span>03</span><h3>启用与设置</h3><p>安装后在插件页启用，按需要调整设置。</p></li></ol></section>`;
}

export function renderCatalog(index, assets) {
  const plugins = index.plugins;
  const counts = { all: plugins.length, tools: plugins.filter(p => !sample(p)).length, samples: plugins.filter(sample).length };
  return shell({ title: '插件市场', description: '发现 FluidBar 的可选插件：文件暂存、倒计时与开发示例。了解用途、权限和安装方式，为小岛添加需要的功能。', pathname: '/market/', assets, body: `
<section class="market-intro"><div><p class="eyebrow">FluidBar 插件市场</p><h1>给你的小岛，<br>添一项本领。</h1><p class="intro-copy">从随手暂存文件，到为一件事计时。<br>把需要的小工具，放进每天都在用的小岛。</p></div><aside class="intro-note">${glyph(PuzzlePiece, 28)}<h2>按需添加，随时停用。</h2><p>目前有 ${plugins.length} 个已发布插件。<br>先看看用途，再决定要不要装进小岛。</p><a class="text-link" href="#install">了解如何安装${glyph(ArrowRight, 17)}</a></aside></section>
<section id="catalog" class="catalog-section" aria-labelledby="catalog-title"><div class="catalog-heading"><h2 id="catalog-title">发现插件</h2><p data-result-count role="status" aria-live="polite">共 ${plugins.length} 个插件</p></div>
<div class="catalog-controls" data-catalog-controls hidden><div class="catalog-filters" role="group" aria-label="按用途筛选">${[['all', '全部插件'], ['tools', '实用工具'], ['samples', '开发示例']].map(([id, label]) => `<button type="button" data-filter="${id}" aria-pressed="${id === 'all'}">${label}<span>${counts[id]}</span></button>`).join('')}</div><label class="catalog-search">${glyph(MagnifyingGlass, 19)}<span class="sr-only">搜索插件</span><input type="search" name="q" placeholder="搜索名称、用途或发布者" autocomplete="off" aria-controls="plugin-grid"></label></div>
<div id="plugin-grid" class="plugin-grid">${plugins.map(card).join('')}</div><div class="catalog-empty" data-empty hidden>${glyph(MagnifyingGlass, 32)}<h3>没有找到匹配的插件</h3><p>换个关键词，或查看全部插件。</p><button type="button" class="text-button" data-reset>清除筛选${glyph(ArrowRight, 16)}</button></div>
<p class="catalog-note">实用工具用于日常操作；开发示例用于了解 SDK 与插件开发。</p></section>
${installGuide()}
<section class="developer-note"><div>${glyph(Code, 28)}<h2>下一项本领，由你来写。</h2><p>从最小示例起步，把自己的想法变成 FluidBar 插件。</p></div>${link(docs, '开始开发')}</section>` });
}

export function renderListing(plugin, history, assets) {
  const name = localized(plugin.name);
  const summary = localized(plugin.summary);
  const latest = plugin.latest;
  const guide = guides[plugin.id];
  const download = available(plugin) && secureUrl(latest.artifact.url);
  const source = guide ? `${repository}/tree/main/${guide.source}` : null;
  const requestList = latest.capabilities || [];
  const title = escape(name);
  const facts = [['版本', `v${latest.version}`], ['安装包', size(latest.artifact.size)], ['更新日期', date(latest.publishedAt)], ['最低软件版本', latest.minAppVersion ? `FluidBar ${latest.minAppVersion}` : '未提供'], ['插件 API', latest.apiVersion || '未提供'], ['许可协议', latest.license || '未提供']];
  return shell({ title: name, description: summary, pathname: href(plugin), detail: true, assets, body: `
<nav class="breadcrumb" aria-label="当前位置"><a href="/market/">${glyph(ArrowLeft, 16)}全部插件</a><span>/</span><span>${title}</span></nav>
<div class="detail-layout"><div class="detail-main"><header class="plugin-heading"><img class="detail-icon" src="/market/assets/${plugin.id}/icon.png" width="88" height="88" alt=""><div><p class="eyebrow">${kindName(plugin)}${guide ? ` · ${escape(guide.eyebrow)}` : ''}</p><h1>${title}</h1><p class="publisher">${publisher(plugin)}<span class="publisher-separator">/</span>v${escape(latest.version)}</p></div></header>
<p class="detail-summary">${escape(summary)}</p><nav class="detail-nav" aria-label="插件内容"><a href="#overview">概览</a><a href="#install">安装</a><a href="#permissions">权限</a><a href="#versions">版本记录</a></nav>
<section id="overview" class="content-section"><h2>${guide ? '让它帮你做这些。' : '插件概览'}</h2><p class="section-lead">${escape(guide?.intro || summary)}</p>${guide ? `<div class="feature-list">${guide.features.map(([heading, copy], i) => `<div class="feature-row"><span class="feature-number">0${i + 1}</span><div><h3>${escape(heading)}</h3><p>${escape(copy)}</p></div></div>`).join('')}</div>` : ''}</section>
${installGuide(name)}<p class="after-install">${escape(guide?.afterInstall || '安装后可在 FluidBar 的插件页查看并管理。')}</p>
<section id="permissions" class="content-section"><h2>它会用到的权限</h2><p class="section-lead">以下为当前版本声明的能力，请在软件内查看并确认授权。</p><dl class="permission-list">${requestList.length ? requestList.map(cap => `<div><dt>${escape(capabilities[cap]?.[0] || cap)}</dt><dd>${escape(capabilities[cap]?.[1] || '此插件声明了该扩展能力，请阅读插件说明。')}</dd></div>`).join('') : '<div><dt>未声明额外能力</dt><dd>安装时以软件显示的信息为准。</dd></div>'}</dl></section>
<section id="versions" class="content-section"><div class="section-heading"><h2>版本记录</h2><a class="text-link" href="/market/plugins/${plugin.id}.json">版本数据${glyph(ArrowUpRight, 15)}</a></div><div class="version-list">${history.versions.map(version => `<article class="version-row"><div><h3>v${escape(version.version)}</h3><span>${version.version === latest.version ? '当前版本' : '历史版本'}${version.yanked ? ' · 已撤回' : ''}</span><time>${date(version.publishedAt)}</time></div><p>${escape(localized(version.changelog) || '此版本暂未提供更新说明。')}</p></article>`).join('')}</div></section></div>
<aside class="plugin-install" aria-label="安装与版本信息"><div class="install-panel"><p class="eyebrow">添加到 FluidBar</p><h2>从软件里安装</h2><p>打开「设置 → 插件市场」，<br>找到「${title}」并安装。</p><div class="package-option"><span>也可以下载后从文件安装</span>${download ? `<a class="download-button" href="${download}">${glyph(DownloadSimple, 19)}下载安装包<span>.fbp</span></a>` : '<p class="unavailable">此版本暂不可下载</p>'}<p class="package-note">${escape(size(latest.artifact.size))} · 安装后需启用</p></div><a class="text-link" href="/#download">还没有 FluidBar？${glyph(ArrowUpRight, 15)}</a></div>
<dl class="plugin-facts">${facts.map(([label, value]) => `<div><dt>${label}</dt><dd>${escape(value)}</dd></div>`).join('')}</dl>
<details class="package-details"><summary>文件校验与手动安装</summary><p>在「设置 → 插件」中选择「从文件安装」，打开下载的 .fbp 文件。</p><div class="hash-heading"><span>SHA-256</span><button type="button" data-copy-hash hidden aria-label="复制 SHA-256 校验值">${glyph(Copy, 15)}复制</button></div><code class="package-hash">${escape(latest.artifact.sha256)}</code><p>软件会执行安装校验。校验值来自当前发布目录。</p></details>
${source ? `<div class="source-link">${link(source, `${glyph(Code, 17)}查看源代码`)}</div>` : ''}</aside></div>
<div class="back-to-market"><a class="text-link" href="/market/">${glyph(ArrowLeft, 17)}继续发现插件</a><span>按需安装，让小岛保持轻盈。</span></div>` });
}
