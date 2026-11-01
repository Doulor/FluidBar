import { ArrowUpRight, Code, FolderSimple, Timer } from '@phosphor-icons/react';
import { PLUGIN_DOCS } from '../lib/links';
import './plugins.css';

export function Plugins() {
  return (
    <section id="plugins" className="plugins section" aria-labelledby="plugins-title">
      <div className="site-shell">
        <header className="plugins-heading"><h2 id="plugins-title" className="section-title">需要什么，<br />再添什么。</h2><p className="section-intro">常用功能已经就位。更多能力，通过插件添加。<br />安装、更新、回滚，都在应用内完成。</p></header>
        <div className="plugin-catalog" aria-label="可选插件">
          <a className="plugin-feature plugin-feature--timer" href="/market/listings/fluidbar-timer/index.html">
            <div className="plugin-feature__symbol"><Timer size={48} weight="light" aria-hidden="true" /></div>
            <div className="plugin-feature__copy"><span className="plugin-type">可选插件</span><h3>倒计时<ArrowUpRight size={23} aria-hidden="true" /></h3><p>设定时间，暂停，继续。时间到了，小岛提醒你。</p></div>
          </a>
          <a className="plugin-feature plugin-feature--files" href="/market/listings/fluidbar-file-shelf/index.html">
            <div className="plugin-feature__symbol"><FolderSimple size={48} weight="light" aria-hidden="true" /></div>
            <div className="plugin-feature__copy"><span className="plugin-type">可选插件</span><h3>文件暂存<ArrowUpRight size={23} aria-hidden="true" /></h3><p>把文件放到手边，需要时拖出复制。原文件留在原处。</p></div>
          </a>
        </div>
        <div className="plugins-bottom"><p>以上插件默认不安装，由你在「设置 → 插件市场」中选择。</p><a className="text-link" href="/market/index.html">浏览插件市场<ArrowUpRight size={15} aria-hidden="true" /></a></div>
        <div className="developer-note"><Code size={28} weight="light" aria-hidden="true" /><div><h3>也欢迎，你的想法。</h3><p>用 Plugin SDK 为小岛添加布局、命令和小组件。渲染交给宿主，专心实现功能。</p></div><a className="text-link" href={PLUGIN_DOCS} target="_blank" rel="noopener noreferrer">开发一个插件<ArrowUpRight size={15} aria-hidden="true" /></a></div>
      </div>
    </section>
  );
}
