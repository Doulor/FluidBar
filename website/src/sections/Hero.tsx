import { ArrowDown, ArrowUpRight } from '@phosphor-icons/react';
import { SpecularButton } from '../ui/reactbits';
import { PrototypeFrame } from '../island/PrototypeFrame';
import './hero.css';

export function Hero() {
  const scrollTo = (id: string) => {
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  return (
    <section id="experience" className="hero" aria-labelledby="hero-title">
      <div className="site-shell hero-grid">
        <div className="hero-copy">
          <p className="kicker">
            <span className="kicker-line" aria-hidden="true" />
            Windows 10 / 11 · FluidBar 2.0
          </p>
          <h1 id="hero-title">
            <span>桌面之上，</span>
            <span>多一座岛。</span>
          </h1>
          <p className="hero-sub">
            常驻，但不打扰。媒体、通知、剪贴板与系统控制，都在一枚小岛里。
          </p>
          <div className="hero-actions">
            <SpecularButton
              className="hero-cta"
              size="md"
              radius={999}
              tint="#0b0d10"
              tintOpacity={1}
              textColor="#f7f8fa"
              baseColor="#0b0d10"
              lineColor="#ffffff"
              intensity={0.8}
              glow={0.3}
              onClick={() => scrollTo('download')}
            >
              下载预览版
              <ArrowUpRight size={16} aria-hidden="true" />
            </SpecularButton>
            <a
              className="text-link"
              href="#story"
              onClick={(event) => {
                event.preventDefault();
                scrollTo('story');
              }}
            >
              先玩一下
              <ArrowDown size={14} aria-hidden="true" />
            </a>
          </div>
          <p className="hero-note">
            源码开放 · MIT · 开发预览版
          </p>
        </div>

        <div className="hero-stage">
          <div className="stage-light" aria-hidden="true" />
          <div className="stage-frame">
            <div className="stage-meta stage-meta--top">
              <span className="live-label">
                <span className="live-dot" aria-hidden="true" />
                LIVE PROTOTYPE
              </span>
              <span className="mono">488 × 444</span>
            </div>

            <PrototypeFrame
              title="FluidBar 真实交互原型：点击小岛展开，按 1 到 6 切换页面"
              name="fluidbar-hero"
              poster={`${import.meta.env.BASE_URL}posters/hero-idle.png`}
              defer
            />

            <div className="stage-meta stage-meta--bottom">
              <span className="mono">132 × 36 · COMPACT</span>
              <span className="mono stage-demo-label">示例数据</span>
              <span className="mono">440 × 284 · EXPANDED</span>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
