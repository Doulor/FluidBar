import { ArrowDown, ArrowUpRight } from '@phosphor-icons/react';
import { SpecularButton } from '../ui/reactbits';
import { PrototypeFrame } from '../island/PrototypeFrame';
import { usePrototype } from '../island/usePrototype';
import { COMPACT_STATE_META } from '../island/compactStates';
import type { CompactView } from '../island/protocol';
import './hero.css';

const PAGES = ['主页', '媒体', '通知', '剪贴板', 'Agent', '小组件'];

export function Hero() {
  const { frameRef, state, setState, send } = usePrototype();

  const scrollTo = (id: string) => {
    window.location.hash = id;
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
    <section id="experience" className="hero chapter chapter--intro" aria-labelledby="hero-title">
      <div className="site-shell hero-grid">
        <div className="hero-copy">
          <p className="kicker">
            <span className="kicker-line" aria-hidden="true" />
            01 / EXPERIENCE · Windows 10 / 11
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
              下载正式版
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
            源码开放 · MIT · 正式版
          </p>

        </div>

        <div id="story" className="hero-stage">
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
              poster={`${import.meta.env.BASE_URL}posters/lab-expanded-daylight.png`}
              startState="expanded"
              onState={setState}
              defer
            />

            <div className="stage-meta stage-meta--bottom">
              <span className="mono">{!state.ready || state.expanded ? '展开 440 × 284' : `${COMPACT_STATE_META.find(item => item.id === state.view)?.label ?? '常驻'} · ${state.width} × ${state.height}`}</span>
              <span className="mono stage-demo-label">{!state.ready || state.expanded ? '示例数据 · 点击右上角收起' : '示例数据 · 点击小岛展开'}</span>
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
          <div className="hero-preview-control">
            <label htmlFor="preview-state">预览状态</label>
            <select
              id="preview-state"
              value={!state.ready || state.expanded ? 'expanded' : state.view}
              onChange={event => {
                frameRef.current?.load();
                send({ state: event.target.value as CompactView | 'expanded' });
              }}
            >
              <option value="expanded">展开控制中心</option>
              {COMPACT_STATE_META.filter(item => ['idle', 'media', 'notifications', 'clipboard', 'agent'].includes(item.id)).map(item => <option key={item.id} value={item.id}>{item.label} · {item.size}</option>)}
            </select>
            <p>收起看活动摘要，展开处理完整内容。预览中可拖动、滚轮或按 1–6 切页。</p>
          </div>
        </div>
      </div>
    </section>
  );
}
