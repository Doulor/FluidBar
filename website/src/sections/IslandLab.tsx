import { useState } from 'react';
import { ArrowUpRight, Check } from '@phosphor-icons/react';
import { PrototypeFrame } from '../island/PrototypeFrame';
import { usePrototype } from '../island/usePrototype';
import type { IslandMaterial, IslandMode } from '../island/protocol';
import './island-lab.css';

const MATERIALS: { id: IslandMaterial; label: string; description: string }[] = [
  { id: 'obsidian', label: '黑曜石', description: '深色实面，让内容清晰浮现。' },
  { id: 'acrylic', label: '亚克力', description: '柔和的模糊，留一点桌面的颜色。' },
  { id: 'liquid', label: '液态玻璃', description: '透亮的边缘，与壁纸自然相融。' },
];
const MODES: { id: IslandMode; label: string; description: string }[] = [
  { id: 'float', label: '浮岛', description: '轻轻悬在屏幕顶部，留出一点呼吸。' },
  { id: 'flush', label: '贴顶', description: '贴合屏幕上沿，融入桌面边界。' },
  { id: 'dock', label: 'Dock', description: '变成桌面底栏，把应用与状态放到手边。' },
];

export function IslandLab() {
  const { frameRef, state, setState, send } = usePrototype();
  const [mode, setMode] = useState<IslandMode>('float');
  const [material, setMaterial] = useState<IslandMaterial>('obsidian');
  return (
    <section id="appearance" className="lab section" aria-labelledby="lab-title">
      <div className="site-shell">
        <header className="lab-heading"><h2 id="lab-title" className="section-title">你的桌面，<br />你的样子。</h2><p className="section-intro">喜欢安静的深色，还是透亮的玻璃？<br />换一种材质，选一个位置，让小岛融入你的桌面。</p></header>
        <div id="material-study" className="lab-grid">
          <div className="lab-stage">
            <PrototypeFrame ref={frameRef} title="FluidBar 真实灵动岛原型" name="fluidbar-lab" startState="expanded" lazy onState={setState} />
            <div className="lab-stage__foot"><span>{state.ready && !state.expanded ? '已收起 · 点击小岛展开' : '展开预览 · 472 × 284'}</span><a href="/island/embed.html" target="_blank" rel="noreferrer" aria-label="打开独立演示"><ArrowUpRight size={16} aria-hidden="true" /></a></div>
          </div>
          <div className="lab-config">
            <div className="control-group">
              <h3>先选一个位置</h3>
              <div className="mode-selector" role="group" aria-label="岛形态">
                {MODES.map(item => <button key={item.id} type="button" className={mode === item.id ? 'is-selected' : ''} aria-pressed={mode === item.id} onClick={() => { frameRef.current?.load(); setMode(item.id); send({ mode: item.id }); }}>{item.label}</button>)}
              </div>
              <p className="mode-description" aria-live="polite">{MODES.find(item => item.id === mode)?.description}</p>
            </div>
            <div className="control-group">
              <h3>再挑一种材质</h3>
              <div className="material-selector" role="group" aria-label="岛材质">
                {MATERIALS.map(item => <button key={item.id} type="button" className={material === item.id ? 'is-selected' : ''} aria-pressed={material === item.id} onClick={() => { frameRef.current?.load(); setMaterial(item.id); send({ material: item.id, state: 'expanded', page: 0 }); }}>
                  <span className={`material-swatch material-swatch--${item.id}`} aria-hidden="true" />
                  <span className="material-choice"><strong>{item.label}</strong><span>{item.description}</span></span>
                  <Check className="material-check" size={17} aria-hidden="true" />
                </button>)}
              </div>
            </div>
            <p className="config-note">网页材质为 CSS 视觉近似，软件由 Skia 绘制。预览使用示例数据，实际界面以正式版为准。</p>
          </div>
        </div>
      </div>
    </section>
  );
}
