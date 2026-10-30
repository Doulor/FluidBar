import { useEffect, useState } from 'react';
import './marketplace.css';

interface MarketPlugin {
  id: string;
  name: Record<string, string>;
  summary: Record<string, string>;
  publisher: { displayName: string };
  latest: { version: string; minAppVersion: string; yanked: boolean };
  categories: string[];
  status: string;
  blocked: unknown;
}
const localize = (text: Record<string, string>) => text['zh-Hans'] || text['en-US'] || '';
const categoryName = (id: string) => ({ developer: '开发工具', media: '媒体' }[id] || id);

export function Marketplace() {
  const [plugins, setPlugins] = useState<MarketPlugin[]>([]);
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [attempt, setAttempt] = useState(0);
  const [query, setQuery] = useState('');
  const [category, setCategory] = useState('all');
  useEffect(() => {
    const controller = new AbortController();
    setStatus('loading');
    fetch(`${import.meta.env.BASE_URL}market/index.json`, { signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error('Market unavailable');
        const index = await response.json();
        if (index.schemaVersion !== 1 || !Array.isArray(index.plugins)) throw new Error('Invalid market index');
        setPlugins(index.plugins.filter((plugin: MarketPlugin) => plugin.status === 'active' && !plugin.blocked && !plugin.latest.yanked));
        setStatus('ready');
      }).catch(() => { if (!controller.signal.aborted) setStatus('error'); });
    return () => controller.abort();
  }, [attempt]);
  const categories = [...new Set(plugins.flatMap(plugin => plugin.categories))];
  const matches = plugins.filter(plugin =>
    (category === 'all' || plugin.categories.includes(category)) &&
    `${localize(plugin.name)} ${localize(plugin.summary)} ${plugin.id} ${plugin.publisher.displayName}`.toLowerCase().includes(query.trim().toLowerCase()),
  );
  return <div id="marketplace" className="market-browser" aria-labelledby="market-title">
    <div className="market-heading"><div><p className="section-kicker">PLUGIN DIRECTORY</p><h3 id="market-title">从真实插件开始。</h3><p>来自当前市场索引。内置能力随软件提供，这里展示独立发布的插件。</p></div><a href="#download">在 FluidBar 内安装与管理 ↗</a></div>
    <div className="market-filters">
      <label>搜索插件<input type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder="名称、用途或发布者" /></label>
      <label>分类<select value={category} onChange={event => setCategory(event.target.value)}><option value="all">全部分类</option>{categories.map(id => <option key={id} value={id}>{categoryName(id)}</option>)}</select></label>
    </div>
    <p className="market-status" role="status">{status === 'loading' ? '正在读取市场…' : status === 'error' ? '暂时无法读取市场，请稍后重试。' : `${matches.length} 个插件${matches.length === 0 ? ' · 没有匹配结果，可调整搜索或分类。' : ''}`}</p>
    {status === 'error' && <button className="market-retry" type="button" onClick={() => setAttempt(value => value + 1)}>重新加载市场</button>}
    {status === 'ready' && <div className="market-list">{matches.map(plugin => <article key={plugin.id} className="market-item">
      <div className="market-item__meta"><span>{plugin.categories.map(categoryName).join(' / ')}</span><span>v{plugin.latest.version}</span></div>
      <h4>{localize(plugin.name)}</h4><p>{localize(plugin.summary)}</p>
      <div className="market-item__foot"><span>{plugin.publisher.displayName}<br />需要 FluidBar ≥ {plugin.latest.minAppVersion}</span><a href={`${import.meta.env.BASE_URL}market/listings/${encodeURIComponent(plugin.id)}/index.html`}>查看详情 ↗<span className="sr-only"> · {localize(plugin.name)}</span></a></div>
    </article>)}</div>}
    <p className="market-note">插件包的验证与安装由应用处理。示例插件用于了解和开发扩展能力。</p>
  </div>;
}
