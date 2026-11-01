import { ArrowUpRight, GithubLogo } from '@phosphor-icons/react';
import { BrandMark } from '../ui/BrandMark';
import { GITHUB, ISSUES, LICENSE, PLUGIN_DOCS, RELEASES } from '../lib/links';
import './footer.css';

export function SiteFooter() {
  return (
    <footer className="site-footer"><div className="site-shell">
      <div className="footer-main"><div><a className="brand" href="#experience" aria-label="FluidBar 首页"><BrandMark size={30} /><span>FluidBar</span></a><p>常驻，但不打扰。</p></div>
        <nav className="footer-links" aria-label="更多链接"><a href={RELEASES} target="_blank" rel="noopener noreferrer">发布记录</a><a href="/market/index.html">插件市场</a><a href={PLUGIN_DOCS} target="_blank" rel="noopener noreferrer">开发文档</a><a href={ISSUES} target="_blank" rel="noopener noreferrer">问题反馈</a></nav>
        <a className="footer-github" href={GITHUB} target="_blank" rel="noopener noreferrer"><GithubLogo size={20} aria-hidden="true" />源代码<ArrowUpRight size={13} aria-hidden="true" /></a>
      </div>
      <div className="footer-bottom"><span>© 2026 Doulor</span><a href={LICENSE} target="_blank" rel="noopener noreferrer">以 MIT 协议开放源代码</a><a href="#experience">回到顶部 ↑</a></div>
    </div></footer>
  );
}
