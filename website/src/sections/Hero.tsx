import { ArrowDown, ArrowUpRight, Bell, ClipboardText, House, MusicNotes, Robot, SquaresFour, WindowsLogo } from '@phosphor-icons/react';
import { PrototypeFrame } from '../island/PrototypeFrame';
import { usePrototype } from '../island/usePrototype';
import { COMPACT_STATE_META } from '../island/compactStates';
import type { CompactView } from '../island/protocol';
import { RELEASE } from '../data/release';
import './hero.css';

const PAGES = [
  { label: '主页', icon: House, title: '常用控制，不必再找。', description: '音量、亮度、系统状态，集中在一个顺手的位置。', compact: 'idle' },
  { label: '媒体', icon: MusicNotes, title: '喜欢的歌，抬眼就看见。', description: '封面、进度和逐词歌词，跟着正在播放的音乐走。', compact: 'media' },
  { label: '通知', icon: Bell, title: '收到消息，留在当前节奏。', description: '浏览通知、查看来源、关闭已读内容，无需反复切换窗口。', compact: 'notifications' },
  { label: '剪贴板', icon: ClipboardText, title: '刚才复制的，还在这里。', description: '找回文本、图片与文件记录，固定常用内容，再次复制。', compact: 'clipboard' },
  { label: 'Agent', icon: Robot, title: '任务在跑，进度在眼前。', description: '查看 Claude Code、Codex 与 DeepSeek Harness 的任务状态。', compact: 'agent' },
  { label: '小组件', icon: SquaresFour, title: '把需要的信息，放在手边。', description: '天气、日历、任务与系统状态，在小岛里一起查看。', compact: 'idle' },
];

export function Hero() {
  const { frameRef, state, setState, send } = usePrototype();
  const activePage = !state.ready ? 1 : state.expanded ? state.page : Math.max(0, PAGES.findIndex(item => item.compact === state.view));
  const active = PAGES[activePage];
  return (
    <section id="experience" className="hero" aria-labelledby="hero-title">
      <div className="site-shell">
        <div className="hero-copy intro-reveal">
          <a className="release-link" href={RELEASE.url} target="_blank" rel="noopener noreferrer">FluidBar {RELEASE.version} <span>正式版现已发布</span><ArrowUpRight size={14} aria-hidden="true" /></a>
          <h1 id="hero-title">让桌面，轻一点。</h1>
          <p className="hero-sub">媒体、通知、剪贴板与系统控制。<br className="mobile-break" />常用的小事，抬手就能处理。</p>
          <div className="hero-actions">
            <a className="button" href={RELEASE.download}><WindowsLogo size={19} aria-hidden="true" />下载 Windows 版</a>
            <a className="text-link" href="#story">试试这座岛<ArrowDown size={16} aria-hidden="true" /></a>
          </div>
        </div>
        <div id="story" className="hero-stage">
        <nav className="hero-page-rail" aria-label="原型页面">
          {PAGES.map(({ label, icon: Icon }, index) => <button key={label} type="button" className={activePage === index ? 'is-active' : ''} aria-pressed={activePage === index} onClick={() => { frameRef.current?.load(); send({ state: 'expanded', page: index }); }}><Icon size={20} aria-hidden="true" />{label}</button>)}
        </nav>
          <PrototypeFrame ref={frameRef} title="FluidBar 真实交互原型：侧边栏切换、拖动或滚轮体验六个页面" name="fluidbar-hero" wide startState="expanded" startPage={1} onState={setState} />
          <div className="hero-stage__caption"><span>你的 Windows，多一个顺手的地方。</span><span>交互演示 · 示例数据</span></div>
        </div>
        <div className="hero-detail">
          <div className="hero-detail__copy" aria-live="polite"><h2>{active.title}</h2><p>{active.description}</p></div>
          <div className="hero-preview-control">
            <label htmlFor="preview-state">预览状态</label>
            <select id="preview-state" value={!state.ready || state.expanded ? 'expanded' : state.view} onChange={event => { frameRef.current?.load(); send({ state: event.target.value as CompactView | 'expanded' }); }}>
              <option value="expanded">展开控制中心</option>
              {COMPACT_STATE_META.filter(item => ['idle', 'media', 'notifications', 'clipboard', 'agent'].includes(item.id)).map(item => <option key={item.id} value={item.id}>{item.label} · {item.size}</option>)}
            </select>
            <p>点击侧边栏切页，拖动、滚轮或按 1–6 也可以。</p>
          </div>
        </div>
        <div className="daily-rhythm">
          <p><strong>看一眼。</strong><span>收起是活动摘要，重要的信息随时可见。</span></p>
          <p><strong>点一下。</strong><span>展开成控制中心，完成眼前的小操作。</span></p>
          <p><strong>接着做。</strong><span>操作完回到小岛，把桌面还给正在做的事。</span></p>
        </div>
      </div>
    </section>
  );
}
