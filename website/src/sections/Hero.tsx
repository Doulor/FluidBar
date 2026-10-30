import { ArrowDown, ArrowUpRight } from '@phosphor-icons/react';
import { SpecularButton } from '../ui/reactbits';
import { PrototypeFrame } from '../island/PrototypeFrame';
import { usePrototype } from '../island/usePrototype';
import './hero.css';

const PAGES = ['主页', '媒体', '通知', '剪贴板', 'Agent', '小组件'];

export function Hero() {
  const { frameRef, state, setState, send } = usePrototype();

  const scrollTo = (id: string) => {
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  const activePage = state.expanded
    ? state.page
    : state.view === 'media'
      ? 1
      : state.view === 'notifications'
        ? 2
        : state.view === 'clipboard'
          ? 3
          : state.view === 'agent'
            ? 4
            : 0;

  return (
    <section id="experience" className="hero" aria-labelledby="hero-title">
      <div className="site-shell hero-grid">
        <div className="hero-copy">
          <p className="kicker">
            <span className="kicker-line" aria-hidden="true" />
            Windows 10 / 11 · 开发预览版
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
          <div className="stage-frame">
            <div className="stage-meta stage-meta--top">
              <span className="live-label">
                <span className="live-dot" aria-hidden="true" />
                交互预览
              </span>
              <span className="mono">主页 · 媒体 · 系统控制</span>
            </div>

            <PrototypeFrame
              ref={frameRef}
              title="FluidBar 真实交互原型：默认展示控制中心，点击小岛可收起"
              name="fluidbar-hero"
              poster={`${import.meta.env.BASE_URL}posters/lab-expanded.png`}
              startState="expanded"
              onState={setState}
              defer
            />

            <div className="stage-meta stage-meta--bottom">
              <span className="mono">展开 440 × 284</span>
              <span className="mono stage-demo-label">示例数据 · 点击右上角收起</span>
            </div>
          </div>

          <nav className="hero-page-rail" aria-label="原型页面">
            {PAGES.map((page, index) => (
              <button
                key={page}
                type="button"
                className={activePage === index ? 'is-active' : ''}
                aria-pressed={activePage === index}
                onClick={() => send({ state: 'expanded', page: index })}
              >
                <span>0{index + 1}</span>
                {page}
              </button>
            ))}
          </nav>
        </div>
      </div>
    </section>
  );
}
