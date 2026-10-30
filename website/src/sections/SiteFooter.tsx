import { ArrowUpRight } from '@phosphor-icons/react';
import { BrandMark } from '../ui/BrandMark';
import {
  GITHUB,
  ISSUES,
  LICENSE,
  MOTION,
  PLUGIN_DOCS,
  REACT_BITS,
  RELEASES,
  SHADERS,
} from '../lib/links';
import './footer.css';

export function SiteFooter() {
  return (
    <footer className="site-footer">
      <div className="site-shell footer-grid">
        <div className="footer-brand">
          <a className="brand" href="#experience">
            <BrandMark size={28} />
            <span className="brand-name">
              FluidBar
              <sup>PREVIEW</sup>
            </span>
          </a>
          <p>常驻，但不打扰。</p>
        </div>

        <div className="footer-links">
          <div>
            <span className="footer-label">产品</span>
            <a href={GITHUB} target="_blank" rel="noopener noreferrer">
              源代码
            </a>
            <a href={RELEASES} target="_blank" rel="noopener noreferrer">
              版本发布
            </a>
            <a href={ISSUES} target="_blank" rel="noopener noreferrer">
              问题反馈
            </a>
          </div>
          <div>
            <span className="footer-label">开发</span>
            <a href={PLUGIN_DOCS} target="_blank" rel="noopener noreferrer">
              插件文档
            </a>
            <a href={`${import.meta.env.BASE_URL}island/embed.html`} target="_blank" rel="noreferrer">
              独立演示
            </a>
            <a href={LICENSE} target="_blank" rel="noopener noreferrer">
              MIT License
            </a>
          </div>
        </div>

        <div className="footer-credits">
          <span className="footer-label">Built with</span>
          <a href={REACT_BITS} target="_blank" rel="noopener noreferrer">
            ReactBits <ArrowUpRight size={11} aria-hidden="true" />
          </a>
          <a href={SHADERS} target="_blank" rel="noopener noreferrer">
            Shaders <ArrowUpRight size={11} aria-hidden="true" />
          </a>
          <a href={MOTION} target="_blank" rel="noopener noreferrer">
            Motion <ArrowUpRight size={11} aria-hidden="true" />
          </a>
        </div>
      </div>

      <div className="site-shell footer-bottom">
        <span>© 2026 Doulor</span>
        <span className="mono">WINDOWS 10 / 11 · ZH-CN</span>
        <a href="#experience">回到顶部</a>
      </div>
    </footer>
  );
}
